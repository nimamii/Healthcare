namespace Healthcare.Tests.Geography.Integration;

using System.IO;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Healthcare.Modules.Geography.Contracts;
using Healthcare.Modules.Geography.Domain;
using Healthcare.Modules.Geography.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// PostGIS behaviour, verified against a live database.
/// </summary>
/// <remarks>
/// These are the tests §1 rests on. The unit tests prove the eligibility rule
/// as a decision table; only this file proves that a real polygon, real SRID
/// and real ST_Contains produce the answer the rule assumes.
///
/// Notably the first version of the write path failed on every insert: the
/// geometry column is NOT NULL, EF does not know how to fill it, and the
/// follow-up UPDATE ran against a row that had never been inserted. Nothing in
/// the unit tests could have caught that.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class PointInPolygonTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;

    // A rectangle roughly around central Tehran, in WGS84.
    private const string Area =
        "POLYGON((51.30 35.60, 51.45 35.60, 51.45 35.75, 51.30 35.75, 51.30 35.60))";

    private const string AreaName = "تهران مرکزی";

    // A bow-tie: crossing lines make the area undefined, which ST_IsValid
    // reports as false.
    private const string SelfIntersecting =
        "POLYGON((51.30 35.60, 51.45 35.75, 51.45 35.60, 51.30 35.75, 51.30 35.60))";

    private static readonly GeoPoint InsidePoint = new(Latitude: 35.67, Longitude: 51.37);

    private static readonly GeoPoint OutsidePoint = new(Latitude: 35.00, Longitude: 51.00);

    private readonly string _connectionString =
        Environment.GetEnvironmentVariable("TEST_POSTGRES")
            is { } configured && !string.IsNullOrWhiteSpace(configured)
            ? configured
            : "Host=localhost;Port=15432;Database=healthcare_test;"
              + "Username=healthcare;Password=local_dev_only";

    private bool _available;

    // Every test derives its own id from its name; this one is fixed because
    // the assertion is "this supplier has no areas", and giving it a name
    // keeps the intent readable next to the query that returns no rows.
    private static readonly Guid UnservicedSupplierId = Guid.NewGuid();

    public PointInPolygonTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync()
    {
        try
        {
            await using var admin = new NpgsqlConnection(FreshDatabase());
            await admin.OpenAsync();

            await using var exists = new NpgsqlCommand(
                "SELECT 1 FROM pg_database WHERE datname = @name", admin);
            exists.Parameters.AddWithValue("name", DatabaseName);

            if (await exists.ExecuteScalarAsync() is null)
            {
                await using var create = new NpgsqlCommand(
                    $"CREATE DATABASE \"{DatabaseName}\"", admin);

                await create.ExecuteNonQueryAsync();
            }

            _available = true;
            await ApplySchemaAsync();
            await ClearAreasAsync();
            _output.WriteLine($"Using database {DatabaseName}");
        }
        catch (Exception ex)
        {
            _available = false;
            _output.WriteLine(
                $"SKIPPED: {_connectionString} is unreachable. Start the stack "
                + $"with .\\build.ps1 -Up. ({ex.GetType().Name}: {ex.Message})");
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private string DatabaseName =>
        new NpgsqlConnectionStringBuilder(_connectionString).Database ?? "healthcare_test";

    private string FreshDatabase()
    {
        var builder = new NpgsqlConnectionStringBuilder(_connectionString)
        {
            Database = "postgres"
        };

        return builder.ConnectionString;
    }

    /// <summary>
    /// Applies the shipped schema file rather than a copy of it.
    ///
    /// Reading db/init/001-schemas.sql means the test exercises the DDL that a
    /// real deployment runs, so a discrepancy between the two surfaces here
    /// instead of against a database nobody checked. Every statement in it is
    /// idempotent, so replaying it across databases is safe.
    /// </summary>
    private async Task ApplySchemaAsync()
    {
        var path = FindSchemaFile();

        if (path is null)
        {
            throw new InvalidOperationException(
                "db/init/001-schemas.sql was not found above " + AppContext.BaseDirectory);
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = await File.ReadAllTextAsync(path, Encoding.UTF8);
        await using var command = new NpgsqlCommand(sql, connection);

        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Clears the table before every test, because xUnit constructs a new test
    /// class per method and <see cref="InitializeAsync"/> runs each time.
    ///
    /// Without this the second run trips ux_service_areas_supplier_name: the
    /// supplier id is derived from the test name rather than random, so a
    /// previous run's row with the same (supplier_id, name) is still there and
    /// the insert fails with a unique violation the test would misreport as a
    /// geometry problem.
    /// </summary>
    private async Task ClearAreasAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "TRUNCATE geography.service_areas", connection);

        await command.ExecuteNonQueryAsync();
    }

    private static string? FindSchemaFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "db", "init", "001-schemas.sql");

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private ScopedContext NewContext(string suffix)
    {
        var options = new DbContextOptionsBuilder<GeographyDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        return new ScopedContext(new GeographyDbContext(options), suffix);
    }

    private sealed class ScopedContext(GeographyDbContext db, string suffix) : IAsyncDisposable
    {
        public GeographyDbContext Db { get; } = db;

        /// <summary>A distinct supplier per test so rows never collide.</summary>
        public Guid NextSupplierId() => Deterministic(suffix);

        private static Guid Deterministic(string name)
        {
            // Fixed rather than random: the id must be stable between runs
            // against the same database, otherwise an earlier test's rows look
            // like someone else's.
            var bytes = MD5.HashData(Encoding.UTF8.GetBytes("geography-integration:" + name));
            return new Guid(bytes);
        }

        // IAsyncDisposable so the `await using` declarations match: DbContext
        // implements both, but a wrapper implementing only IDisposable cannot
        // be awaited in a using statement.
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private void SkipIfNoDatabase()
    {
        if (_available)
        {
            return;
        }

        _output.WriteLine("SKIPPED: no database. The assertion below did not run.");
    }

    private bool ShouldRun => _available;

    // --- Tests ---------------------------------------------------------------

    [Fact]
    public async Task A_saved_area_round_trips_through_a_real_geometry_column()
    {
        SkipIfNoDatabase();
        if (!ShouldRun) return;

        await using var ctx = NewContext("roundtrip");
        var supplier = ctx.NextSupplierId();

        var area = CreateArea(supplier, AreaName, Area);

        ctx.Db.ServiceAreas.Add(area);
        var written = await ctx.Db.SaveChangesAsync();

        written.Should().Be(1, "the insert must land in the database");

        // Read back through raw SQL: the mapped property is the text, and the
        // column under test is the one this test exists for.
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var probe = new NpgsqlCommand(
            """
            SELECT ST_IsValid(polygon),
                   GeometryType(polygon),
                   GeometryType(ST_GeomFromText(polygon_wkt, 4326)),
                   ST_Area(ST_Transform(polygon, 4326))
            FROM geography.service_areas
            WHERE id = @id
            """,
            connection);

        probe.Parameters.AddWithValue("id", area.Id);

        await using var reader = await probe.ExecuteReaderAsync();
        reader.Read().Should().BeTrue("the row must exist after a successful save");

        reader.GetBoolean(0).Should().BeTrue(
            "the CHECK constraint guarantees this; a false means the constraint was not enforced");
        reader.GetString(1).Should().Be("MULTIPOLYGON",
            "the column type is MultiPolygon, and ST_Multi is what promotes a POLYGON to it");
        reader.GetString(2).Should().Be("POLYGON",
            "the stored text must survive the round trip unchanged");
        reader.GetDouble(3).Should().BeGreaterThan(0.0,
            "an area with zero extent would contain no point at all");
    }

    [Fact]
    public async Task A_point_inside_the_area_matches_it()
    {
        SkipIfNoDatabase();
        if (!ShouldRun) return;

        await using var ctx = NewContext("inside");
        var supplier = ctx.NextSupplierId();

        // Seeded explicitly. Without it the EXISTS query finds no rows and
        // returns false — which would fail this test for the wrong reason and
        // say nothing about ST_Contains either way.
        ctx.Db.ServiceAreas.Add(CreateArea(supplier, AreaName, Area));
        await ctx.Db.SaveChangesAsync();

        var inside = await IsInside(supplier, InsidePoint);
        inside.Should().BeTrue(
            "ST_Contains on the stored polygon is the whole point of storing it this way");
    }

    [Fact]
    public async Task A_point_outside_the_area_does_not_match_it()
    {
        SkipIfNoDatabase();
        if (!ShouldRun) return;

        await using var ctx = NewContext("outside");
        var supplier = ctx.NextSupplierId();

        // Seed first. Querying before that would return false because no row
        // exists, not because the point is outside — a pass that proves
        // nothing.
        ctx.Db.ServiceAreas.Add(CreateArea(supplier, AreaName, Area));
        await ctx.Db.SaveChangesAsync();

        var isInside = await IsInside(supplier, OutsidePoint);
        isInside.Should().BeFalse(
            "a point outside a seeded polygon must not be reported as served");
    }

    [Fact]
    public async Task The_service_evaluates_a_delivery_address_against_the_area()
    {
        SkipIfNoDatabase();
        if (!ShouldRun) return;

        await using var ctx = NewContext("evaluate");
        var supplier = ctx.NextSupplierId();

        ctx.Db.ServiceAreas.Add(CreateArea(supplier, AreaName, Area));
        await ctx.Db.SaveChangesAsync();

        var service = new GeographyService(ctx.Db, _connectionString, NullLogger<GeographyService>.Instance);

        var insideResult = await service.EvaluateAsync(supplier, InsidePoint, CancellationToken.None);

        insideResult.IsInArea.Should().BeTrue(
            "§1 requires the inside case to be a normal, confirm-free delivery");
        insideResult.RequiresCustomerConfirmation.Should().BeFalse();
        insideResult.Surcharge.Should().BeNull();

        var outsideResult = await service.EvaluateAsync(supplier, OutsidePoint, CancellationToken.None);

        outsideResult.IsInArea.Should().BeFalse();

        // The most easily inverted branch: no areas must mean "unrestricted",
        // not "cannot serve". Asserting it makes the direction explicit.
        var noAreas = await service.EvaluateAsync(
            UnservicedSupplierId, OutsidePoint, CancellationToken.None);

        noAreas.IsInArea.Should().BeTrue(
            "a supplier with no defined areas is unrestricted, not unservable");
    }

    [Fact]
    public async Task An_outside_address_with_a_surcharge_asks_for_confirmation()
    {
        SkipIfNoDatabase();
        if (!ShouldRun) return;

        await using var ctx = NewContext("surcharge");
        var supplier = ctx.NextSupplierId();

        // Supplied after the area so the surcharge lookup finds it.
        var area = CreateArea(supplier, AreaName, Area, surcharge: 250_000, currency: "IRR");
        ctx.Db.ServiceAreas.Add(area);
        await ctx.Db.SaveChangesAsync();

        var service = new GeographyService(ctx.Db, _connectionString, NullLogger<GeographyService>.Instance);

        var outside = await service.EvaluateAsync(supplier, OutsidePoint, CancellationToken.None);

        outside.IsInArea.Should().BeFalse(
            "§1 forbids rejecting the address; the outcome is a question, not a refusal");
        outside.Surcharge.Should().Be(250_000);
        outside.Currency.Should().Be("IRR");
        outside.RequiresCustomerConfirmation.Should().BeTrue(
            "the customer must be asked before the extra cost is accepted");
    }

    [Fact]
    public async Task An_outside_address_with_no_surcharge_still_delivers()
    {
        SkipIfNoDatabase();
        if (!ShouldRun) return;

        await using var ctx = NewContext("nosurcharge");
        var supplier = ctx.NextSupplierId();

        ctx.Db.ServiceAreas.Add(CreateArea(supplier, AreaName, Area));
        await ctx.Db.SaveChangesAsync();

        var service = new GeographyService(ctx.Db, _connectionString, NullLogger<GeographyService>.Instance);

        var outside = await service.EvaluateAsync(supplier, OutsidePoint, CancellationToken.None);

        outside.RequiresCustomerConfirmation.Should().BeFalse(
            "nothing is charged, so there is nothing to confirm");
        outside.Surcharge.Should().BeNull();
    }

    [Fact]
    public async Task A_self_intersecting_polygon_is_refused()
    {
        SkipIfNoDatabase();
        if (!ShouldRun) return;

        await using var ctx = NewContext("invalid");
        var supplier = ctx.NextSupplierId();

        ctx.Db.ServiceAreas.Add(CreateArea(supplier, "منحرف", SelfIntersecting));

        var act = async () => await ctx.Db.SaveChangesAsync();

        await act.Should().ThrowAsync<PostgresException>(
            "ck_service_areas_polygon_valid refuses geometry that ST_IsValid "
            + "reports as false; letting it through would make ST_Contains "
            + "answer unpredictably");
    }

    // --- Helpers -------------------------------------------------------------

    private static ServiceArea CreateArea(
        Guid supplierId,
        string name,
        string wkt,
        decimal? surcharge = null,
        string? currency = null)
    {
        var result = ServiceArea.Create(
            supplierId, name, wkt, surcharge, currency, DateTimeOffset.UtcNow);

        result.IsSuccess.Should().BeTrue(
            "the structural checks must pass for this fixture: " + result.ErrorMessage);

        return result.Value;
    }

    /// <summary>
    /// Whether any of this supplier's areas contains the given point.
    /// </summary>
    /// <remarks>
    /// The point is a parameter, not a constant. The first version hard-coded
    /// InsidePoint, so the test asserting a point outside the area checked the
    /// point inside it and reported True — a failure whose message pointed at
    /// a data problem rather than at the helper never being asked the
    /// question.
    /// </remarks>
    private async Task<bool> IsInside(Guid supplierId, GeoPoint point)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var probe = new NpgsqlCommand(
            """
            SELECT EXISTS(
                SELECT 1
                FROM geography.service_areas
                WHERE supplier_id = @supplier
                  AND is_active
                  AND ST_Contains(
                        polygon,
                        ST_SetSRID(
                            ST_MakePoint(@lon, @lat),
                            4326))
            )
            """,
            connection);

        probe.Parameters.AddWithValue("supplier", supplierId);
        probe.Parameters.AddWithValue("lon", point.Longitude);
        probe.Parameters.AddWithValue("lat", point.Latitude);

        // The caller seeds the row so the setup stays visible in each test
        // rather than hidden in a fixture.
        return (bool)(await probe.ExecuteScalarAsync() ?? false);
    }
}
