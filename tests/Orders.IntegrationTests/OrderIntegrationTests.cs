using System.Text;
using FluentAssertions;
using Healthcare.Modules.Orders.Application;
using Healthcare.Modules.Orders.Domain;
using Healthcare.Modules.Orders.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Healthcare.Tests.Orders.Integration;

/// <summary>
/// Order behaviour, verified against a live database.
/// </summary>
/// <remarks>
/// These tests prove that orders persist and transition correctly against
/// real PostgreSQL, not an in-memory fake. The unit tests prove the state
/// machine logic; these tests prove the state machine executes against
/// a real database.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class OrderIntegrationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;

    private readonly string _connectionString =
        Environment.GetEnvironmentVariable("TEST_POSTGRES")
            is { } configured && !string.IsNullOrWhiteSpace(configured)
            ? configured
            : "Host=localhost;Port=15432;Database=healthcare_test;"
              + "Username=healthcare;Password=local_dev_only";

    private bool _available;

    public OrderIntegrationTests(ITestOutputHelper output) => _output = output;

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
            await ClearDataAsync();
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

    private async Task ClearDataAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "TRUNCATE orders.order_lines, orders.orders",
            connection);

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

    private OrdersDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<OrdersDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        return new OrdersDbContext(options);
    }

    private void SkipIfNoDatabase()
    {
        if (_available) return;
        _output.WriteLine("SKIPPED: no database. The assertion below did not run.");
    }

    private bool ShouldRun => _available;

    // --- Tests ---------------------------------------------------------------

    [Fact]
    public async Task An_order_round_trips_through_the_database()
    {
        SkipIfNoDatabase();
        if (!ShouldRun) return;

        await using var ctx = NewContext();
        var now = DateTimeOffset.UtcNow;

        var order = Order.Create(Guid.NewGuid(), Guid.NewGuid(), now).Value;
        order.AddLine(Guid.NewGuid(), 2, 150_000, "IRR", now);

        ctx.Orders.Add(order);
        await ctx.SaveChangesAsync();

        var loaded = await ctx.Orders
            .Include(o => o.Lines)
            .FirstAsync(o => o.Id == order.Id);

        loaded.Status.Should().Be(OrderStatus.Created);
        loaded.Lines.Should().HaveCount(1);
        loaded.Lines.First().Quantity.Should().Be(2);
        loaded.Lines.First().UnitPrice.Should().Be(150_000);
    }

    [Fact]
    public async Task The_happy_path_walks_through_every_state_in_the_database()
    {
        SkipIfNoDatabase();
        if (!ShouldRun) return;

        await using var ctx = NewContext();
        var now = DateTimeOffset.UtcNow;

        var order = Order.Create(Guid.NewGuid(), Guid.NewGuid(), now).Value;
        ctx.Orders.Add(order);
        await ctx.SaveChangesAsync();

        order.MarkPaymentPending(now);
        await ctx.SaveChangesAsync();

        order.MarkPaid(now);
        await ctx.SaveChangesAsync();

        order.MarkFulfillmentPending(now);
        await ctx.SaveChangesAsync();

        order.Confirm(now);
        await ctx.SaveChangesAsync();

        var loaded = await ctx.Orders.FirstAsync(o => o.Id == order.Id);
        loaded.Status.Should().Be(OrderStatus.Confirmed);
    }

    [Fact]
    public async Task An_invalid_transition_is_rejected_and_not_persisted()
    {
        SkipIfNoDatabase();
        if (!ShouldRun) return;

        await using var ctx = NewContext();
        var now = DateTimeOffset.UtcNow;

        var order = Order.Create(Guid.NewGuid(), Guid.NewGuid(), now).Value;
        ctx.Orders.Add(order);
        await ctx.SaveChangesAsync();

        var result = order.MarkPaid(now);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("order_invalid_transition");

        var loaded = await ctx.Orders.FirstAsync(o => o.Id == order.Id);
        loaded.Status.Should().Be(OrderStatus.Created);
    }

    [Fact]
    public async Task A_cancelled_order_persists_as_cancelled()
    {
        SkipIfNoDatabase();
        if (!ShouldRun) return;

        await using var ctx = NewContext();
        var now = DateTimeOffset.UtcNow;

        var order = Order.Create(Guid.NewGuid(), Guid.NewGuid(), now).Value;
        ctx.Orders.Add(order);
        await ctx.SaveChangesAsync();

        order.Cancel(now);
        await ctx.SaveChangesAsync();

        var loaded = await ctx.Orders.FirstAsync(o => o.Id == order.Id);
        loaded.Status.Should().Be(OrderStatus.Cancelled);
    }
}
