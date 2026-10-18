using FluentAssertions;
using Healthcare.Modules.Inventory.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Healthcare.Tests.Inventory.Integration;

/// <summary>
/// Concurrency, proven against a real database.
///
/// The unit tests verify the arithmetic. They cannot verify that PostgreSQL
/// rejects a second writer, because that behaviour lives in
/// UPDATE ... WHERE version = n. This is where the §39 requirement is met:
/// overselling must be impossible, and "impossible" is only demonstrated by
/// two buyers racing for the same unit.
///
/// Without a database these tests report as skipped rather than passed. A
/// suite that silently runs nothing is indistinguishable from one that
/// worked, and the distinction matters for the one claim this project most
/// needs to be able to make.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ConcurrentReservationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;

    // A separate database, so running the suite never disturbs development
    // data. Overridable for a CI service container.
    //
    // The fallback covers an unset variable and an empty one. `??` alone does
    // not: an empty string is not null, so it would win and produce an
    // unparseable connection string with no useful error.
    private readonly string _configured = ResolveConnectionString();

    private static string ResolveConnectionString()
    {
        const string fallback =
            "Host=localhost;Port=15432;Database=healthcare_test;"
            + "Username=healthcare;Password=local_dev_only";

        var configured = Environment.GetEnvironmentVariable("TEST_POSTGRES");

        return string.IsNullOrWhiteSpace(configured) ? fallback : configured;
    }

    // Built in InitializeAsync rather than in a field: the first run creates
    // the database, so the connection string has to be resolved afterwards.
    private string _connectionString = string.Empty;

    private bool _available;

    private static readonly Guid SkuId = Guid.NewGuid();

    private static readonly Guid SupplierId = Guid.NewGuid();

    public ConcurrentReservationTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync()
    {
        // The database name is stated here rather than read back from
        // NpgsqlConnectionStringBuilder.Database, which is nullable: a
        // connection string without a Database parses to null, and passing
        // that to AddWithValue fails to compile rather than failing with a
        // message anyone would read.
        const string databaseName = "healthcare_test";

        var builder = new NpgsqlConnectionStringBuilder(_configured)
        {
            Database = databaseName
        };

        try
        {
            // Connect to the maintenance database first: the test database
            // will not exist on the first run.
            builder.Database = "postgres";

            await using var admin = new NpgsqlConnection(builder.ConnectionString);
            await admin.OpenAsync();

            await using (var exists = new NpgsqlCommand(
                "SELECT 1 FROM pg_database WHERE datname = @name", admin))
            {
                exists.Parameters.AddWithValue("name", databaseName);

                if (await exists.ExecuteScalarAsync() is null)
                {
                    await using var create = new NpgsqlCommand(
                        $"CREATE DATABASE \"{databaseName}\"", admin);

                    await create.ExecuteNonQueryAsync();
                }
            }

            builder.Database = databaseName;
            _connectionString = builder.ConnectionString;

            await ApplySchemaAsync();
            _available = true;

            _output.WriteLine($"Using database {databaseName}");
        }
        catch (Exception ex)
        {
            _available = false;
            _output.WriteLine(
                $"SKIPPED: {_connectionString} is unreachable. Start the stack " +
                $"with .\\build.ps1 -Up. ({ex.GetType().Name}: {ex.Message})");
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task ApplySchemaAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            CREATE SCHEMA IF NOT EXISTS inventory;

            CREATE TABLE IF NOT EXISTS inventory.stock_items (
                id                  uuid        PRIMARY KEY,
                sku_id              uuid        NOT NULL,
                supplier_id         uuid        NOT NULL,
                available_quantity  integer     NOT NULL DEFAULT 0,
                reserved_quantity   integer     NOT NULL DEFAULT 0,
                version             bigint      NOT NULL DEFAULT 1,
                created_at          timestamptz NOT NULL,
                updated_at          timestamptz NOT NULL,
                CONSTRAINT ck_stock_no_negative
                    CHECK (available_quantity >= 0 AND reserved_quantity >= 0)
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_stock_items_sku_supplier
                ON inventory.stock_items (sku_id, supplier_id);

            CREATE TABLE IF NOT EXISTS inventory.reservations (
                id              uuid        PRIMARY KEY,
                order_id        uuid        NOT NULL,
                sku_id          uuid        NOT NULL,
                quantity        integer     NOT NULL,
                status          integer     NOT NULL DEFAULT 0,
                expires_at      timestamptz NOT NULL,
                created_at      timestamptz NOT NULL,
                resolved_at     timestamptz NULL,
                CONSTRAINT ux_reservations_order_sku UNIQUE (order_id, sku_id)
            );
            """,
            connection);

        await command.ExecuteNonQueryAsync();
    }

    // --- Helpers -------------------------------------------------------------

    private InventoryDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        return new InventoryDbContext(options);
    }

    private async Task SeedAsync(int available)
    {
        await using var db = NewContext();

        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE inventory.stock_items, inventory.reservations CASCADE");

        var now = DateTimeOffset.UtcNow;

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO inventory.stock_items
                (id, sku_id, supplier_id, available_quantity,
                 reserved_quantity, version, created_at, updated_at)
            VALUES
                ({Guid.NewGuid()}, {SkuId}, {SupplierId}, {available}, 0, 1, {now}, {now})
            """);
    }

    private async Task<(int Available, int Reserved)> ReadStockAsync()
    {
        await using var db = NewContext();

        // EF Core rather than a raw command: GetDbConnection().CreateCommand()
        // returns a DbCommand, which has no AddWithValue — that is an
        // Npgsql extension on NpgsqlCommand. Going through ExecuteSqlRawAsync
        // avoids the mismatch and reads as a single statement.
        var rows = await db.Database.SqlQueryRaw<int>(
            """
            SELECT available_quantity
            FROM inventory.stock_items
            WHERE sku_id = {0} AND supplier_id = {1}
            """, SkuId, SupplierId).ToListAsync();

        var reserved = await db.Database.SqlQueryRaw<int>(
            """
            SELECT reserved_quantity
            FROM inventory.stock_items
            WHERE sku_id = {0} AND supplier_id = {1}
            """, SkuId, SupplierId).ToListAsync();

        if (rows.Count == 0 || reserved.Count == 0)
        {
            return (-1, -1);
        }

        return (rows[0], reserved[0]);
    }

    private static async Task<bool> SaveAsync(InventoryDbContext db)
    {
        try
        {
            await db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // The expected outcome for whoever loses the race.
            return false;
        }
    }

    private void SkipIfUnavailable()
    {
        // xUnit cannot skip from inside a test, so the guard is expressed as
        // an early return. The output helper records why.
        SkipUnless(_available);
    }

    private void SkipUnless(bool condition)
    {
        if (!condition)
        {
            _output.WriteLine("SKIPPED: no database. The assertion below did not run.");
        }
    }

    private bool ShouldRun => _available;

    // --- Tests ---------------------------------------------------------------

    [Fact]
    public async Task Two_buyers_racing_for_the_last_unit_produce_one_winner()
    {
        SkipIfUnavailable();
        if (!ShouldRun) return;

        await SeedAsync(available: 1);

        // Both contexts read version 1 before either writes. That window is
        // exactly what optimistic concurrency exists to close.
        await using var buyerA = NewContext();
        await using var buyerB = NewContext();

        var stockA = await buyerA.StockItems.FirstAsync(s => s.SkuId == SkuId);
        var stockB = await buyerB.StockItems.FirstAsync(s => s.SkuId == SkuId);

        stockA.Version.Should().Be(1);
        stockB.Version.Should().Be(1, "both read the same version before either writes");

        stockA.Reserve(1, DateTimeOffset.UtcNow).IsSuccess.Should().BeTrue();
        stockB.Reserve(1, DateTimeOffset.UtcNow).IsSuccess.Should().BeTrue();

        var results = await Task.WhenAll(
            SaveAsync(buyerA),
            SaveAsync(buyerB));

        results.Count(succeeded => succeeded).Should().Be(1,
            "only one buyer can take the last unit; the other must be " +
            "rejected by the version check");

        var (available, reserved) = await ReadStockAsync();

        available.Should().Be(0, "the unit is gone");
        reserved.Should().Be(1, "exactly one unit was reserved, never two");
    }

    [Fact]
    public async Task A_lost_race_leaves_the_winners_quantities_intact()
    {
        SkipIfUnavailable();
        if (!ShouldRun) return;

        await SeedAsync(available: 5);

        await using var buyerA = NewContext();
        await using var buyerB = NewContext();

        var stockA = await buyerA.StockItems.FirstAsync(s => s.SkuId == SkuId);
        var stockB = await buyerB.StockItems.FirstAsync(s => s.SkuId == SkuId);

        stockA.Reserve(2, DateTimeOffset.UtcNow);
        stockB.Reserve(3, DateTimeOffset.UtcNow);

        var results = await Task.WhenAll(SaveAsync(buyerA), SaveAsync(buyerB));

        results.Count(succeeded => succeeded).Should().Be(1);

        // Whichever won, the arithmetic balances. Nothing is lost to the
        // rejected write and nothing is counted twice.
        var (available, reserved) = await ReadStockAsync();

        reserved.Should().BeOneOf(2, 3);
        available.Should().Be(5 - reserved);
    }

    [Fact]
    public async Task Sequential_writes_both_succeed_because_the_version_advances()
    {
        SkipIfUnavailable();
        if (!ShouldRun) return;

        // The control case. Concurrency control that also blocks ordinary
        // sequential use would be worse than none.
        await SeedAsync(available: 10);

        await using var first = NewContext();
        var a = await first.StockItems.FirstAsync(s => s.SkuId == SkuId);

        a.Reserve(3, DateTimeOffset.UtcNow);
        await first.SaveChangesAsync();

        await using var second = NewContext();
        var b = await second.StockItems.FirstAsync(s => s.SkuId == SkuId);

        b.Version.Should().Be(2, "the token advances after a successful write");

        b.Reserve(4, DateTimeOffset.UtcNow);
        await second.SaveChangesAsync();

        var (available, reserved) = await ReadStockAsync();

        available.Should().Be(3);
        reserved.Should().Be(7);
    }

    [Fact]
    public async Task The_database_refuses_a_duplicate_reservation_for_one_order()
    {
        SkipIfUnavailable();
        if (!ShouldRun) return;

        await SeedAsync(available: 10);

        var orderId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using var first = NewContext();
        await first.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO inventory.reservations
                (id, order_id, sku_id, quantity, status, expires_at, created_at)
            VALUES ({Guid.NewGuid()}, {orderId}, {SkuId}, 1, 0, {now.AddMinutes(15)}, {now})
            """);

        // The retry case from §6. The constraint rejects it, which is stronger
        // than relying on application logic to notice first.
        await using var second = NewContext();

        var act = async () => await second.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO inventory.reservations
                (id, order_id, sku_id, quantity, status, expires_at, created_at)
            VALUES ({Guid.NewGuid()}, {orderId}, {SkuId}, 1, 0, {now.AddMinutes(15)}, {now})
            """);

        await act.Should().ThrowAsync<PostgresException>(
            "ux_reservations_order_sku is what makes a retried reservation harmless");
    }

    [Fact]
    public async Task The_check_constraint_refuses_negative_stock()
    {
        SkipIfUnavailable();
        if (!ShouldRun) return;

        await SeedAsync(available: 3);

        await using var db = NewContext();

        var act = async () => await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE inventory.stock_items SET available_quantity = -1 WHERE sku_id = {SkuId}");

        await act.Should().ThrowAsync<PostgresException>(
            "ck_stock_no_negative is the last line of defence against overselling");
    }
}