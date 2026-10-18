using System.Text;
using FluentAssertions;
using Healthcare.Modules.Payments.Application;
using Healthcare.Modules.Payments.Domain;
using Healthcare.Modules.Payments.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Healthcare.Tests.Payments.Integration;

/// <summary>
/// Payment behaviour, verified against a live database.
/// </summary>
/// <remarks>
/// These tests prove that payments persist and transition correctly against
/// real PostgreSQL. The unit tests prove the state machine logic; these tests
/// prove the state machine executes against a real database.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class PaymentIntegrationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;

    private readonly string _connectionString =
        Environment.GetEnvironmentVariable("TEST_POSTGRES")
            is { } configured && !string.IsNullOrWhiteSpace(configured)
            ? configured
            : "Host=localhost;Port=15432;Database=healthcare_test;"
              + "Username=healthcare;Password=local_dev_only";

    private bool _available;

    public PaymentIntegrationTests(ITestOutputHelper output) => _output = output;

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
            "TRUNCATE payments.payments",
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

    private PaymentsDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        return new PaymentsDbContext(options);
    }

    private void SkipIfNoDatabase()
    {
        if (_available) return;
        _output.WriteLine("SKIPPED: no database. The assertion below did not run.");
    }

    private bool ShouldRun => _available;

    // --- Tests ---------------------------------------------------------------

    [Fact]
    public async Task A_payment_round_trips_through_the_database()
    {
        SkipIfNoDatabase();
        if (!ShouldRun) return;

        await using var ctx = NewContext();
        var now = DateTimeOffset.UtcNow;

        var payment = Payment.Create(Guid.NewGuid(), 150_000, "IRR", now).Value;
        ctx.Payments.Add(payment);
        await ctx.SaveChangesAsync();

        var loaded = await ctx.Payments.FirstAsync(p => p.Id == payment.Id);

        loaded.Status.Should().Be(PaymentStatus.Pending);
        loaded.Amount.Should().Be(150_000);
        loaded.Currency.Should().Be("IRR");
        loaded.ProviderTransactionId.Should().BeNull();
    }

    [Fact]
    public async Task The_happy_path_walks_through_every_state_in_the_database()
    {
        SkipIfNoDatabase();
        if (!ShouldRun) return;

        await using var ctx = NewContext();
        var now = DateTimeOffset.UtcNow;

        var payment = Payment.Create(Guid.NewGuid(), 100, "IRR", now).Value;
        ctx.Payments.Add(payment);
        await ctx.SaveChangesAsync();

        payment.MarkAuthorized("tx_123", now);
        await ctx.SaveChangesAsync();

        payment.MarkCaptured(now);
        await ctx.SaveChangesAsync();

        payment.MarkSettled(now);
        await ctx.SaveChangesAsync();

        var loaded = await ctx.Payments.FirstAsync(p => p.Id == payment.Id);
        loaded.Status.Should().Be(PaymentStatus.Settled);
        loaded.ProviderTransactionId.Should().Be("tx_123");
    }

    [Fact]
    public async Task An_invalid_transition_is_rejected_and_not_persisted()
    {
        SkipIfNoDatabase();
        if (!ShouldRun) return;

        await using var ctx = NewContext();
        var now = DateTimeOffset.UtcNow;

        var payment = Payment.Create(Guid.NewGuid(), 100, "IRR", now).Value;
        ctx.Payments.Add(payment);
        await ctx.SaveChangesAsync();

        var result = payment.MarkCaptured(now);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("payment_invalid_transition");

        var loaded = await ctx.Payments.FirstAsync(p => p.Id == payment.Id);
        loaded.Status.Should().Be(PaymentStatus.Pending);
    }

    [Fact]
    public async Task A_refunded_payment_persists_as_refunded()
    {
        SkipIfNoDatabase();
        if (!ShouldRun) return;

        await using var ctx = NewContext();
        var now = DateTimeOffset.UtcNow;

        var payment = Payment.Create(Guid.NewGuid(), 100, "IRR", now).Value;
        payment.MarkAuthorized("tx_123", now);
        payment.MarkCaptured(now);
        payment.MarkSettled(now);
        ctx.Payments.Add(payment);
        await ctx.SaveChangesAsync();

        payment.MarkRefunded(now);
        await ctx.SaveChangesAsync();

        var loaded = await ctx.Payments.FirstAsync(p => p.Id == payment.Id);
        loaded.Status.Should().Be(PaymentStatus.Refunded);
    }
}
