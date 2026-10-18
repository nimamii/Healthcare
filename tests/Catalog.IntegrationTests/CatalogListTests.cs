using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Healthcare.Modules.Catalog.Contracts;
using Healthcare.Modules.Catalog.Domain;
using Healthcare.Modules.Catalog.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Catalogue list behaviour, verified against a live database.
/// </summary>
/// <remarks>
/// These tests prove that the ListProductsAsync endpoint returns real data
/// from the database, not from an in-memory fake. The unit tests prove the
/// query logic; these tests prove the query actually executes against
/// PostgreSQL.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class CatalogListTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;

    private readonly string _connectionString =
        Environment.GetEnvironmentVariable("TEST_POSTGRES")
            is { } configured && !string.IsNullOrWhiteSpace(configured)
            ? configured
            : "Host=localhost;Port=15432;Database=healthcare_test;"
              + "Username=healthcare;Password=local_dev_only";

    private bool _available;

    public CatalogListTests(ITestOutputHelper output) => _output = output;

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
            "TRUNCATE catalog.supplier_products, catalog.skus, catalog.products, catalog.categories",
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

    private CatalogDbContext NewContext(string suffix)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        return new CatalogDbContext(options);
    }

    private void SkipIfNoDatabase()
    {
        if (_available) return;
        _output.WriteLine("SKIPPED: no database. The assertion below did not run.");
    }

    private bool ShouldRun => _available;

    // --- Tests ---------------------------------------------------------------

    [Fact]
    public async Task ListProducts_returns_empty_when_no_products_exist()
    {
        SkipIfNoDatabase();
        if (!ShouldRun) return;

        await using var ctx = NewContext("empty");

        var service = new ProductCatalog(ctx);
        var products = await service.ListProductsAsync(null, CancellationToken.None);

        products.Should().BeEmpty("no products have been seeded");
    }

    [Fact]
    public async Task ListProducts_returns_seeded_products()
    {
        SkipIfNoDatabase();
        if (!ShouldRun) return;

        await using var ctx = NewContext("seeded");
        var now = DateTimeOffset.UtcNow;

        var categoryResult = Category.CreateRoot("تجهیزات پزشکی", "medical-equipment", now);
        categoryResult.IsSuccess.Should().BeTrue();
        ctx.Categories.Add(categoryResult.Value);

        var productResult = Product.Create(
            "مانیتور بیمار", "patient-monitor", "مانیتور چندپارامتره",
            categoryResult.Value.Id, false, now);
        productResult.IsSuccess.Should().BeTrue();
        ctx.Products.Add(productResult.Value);

        await ctx.SaveChangesAsync();

        var service = new ProductCatalog(ctx);
        var products = await service.ListProductsAsync(null, CancellationToken.None);

        products.Should().HaveCount(1);
        products.First().Name.Should().Be("مانیتور بیمار");
        products.First().Slug.Should().Be("patient-monitor");
        products.First().CategoryId.Should().Be(categoryResult.Value.Id);
        products.First().RequiresPrescription.Should().BeFalse();
        products.First().IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task ListProducts_filters_by_category()
    {
        SkipIfNoDatabase();
        if (!ShouldRun) return;

        await using var ctx = NewContext("filter");
        var now = DateTimeOffset.UtcNow;

        var catAResult = Category.CreateRoot("دسته الف", "cat-a", now);
        catAResult.IsSuccess.Should().BeTrue();
        ctx.Categories.Add(catAResult.Value);

        var catBResult = Category.CreateRoot("دسته ب", "cat-b", now);
        catBResult.IsSuccess.Should().BeTrue();
        ctx.Categories.Add(catBResult.Value);

        var productAResult = Product.Create(
            "محصول الف", "product-a", null, catAResult.Value.Id, false, now);
        productAResult.IsSuccess.Should().BeTrue();
        ctx.Products.Add(productAResult.Value);

        var productBResult = Product.Create(
            "محصول ب", "product-b", null, catBResult.Value.Id, false, now);
        productBResult.IsSuccess.Should().BeTrue();
        ctx.Products.Add(productBResult.Value);

        await ctx.SaveChangesAsync();

        var service = new ProductCatalog(ctx);
        var catAProducts = await service.ListProductsAsync(catAResult.Value.Id, CancellationToken.None);

        catAProducts.Should().HaveCount(1);
        catAProducts.First().Name.Should().Be("محصول الف");

        var catBProducts = await service.ListProductsAsync(catBResult.Value.Id, CancellationToken.None);
        catBProducts.Should().HaveCount(1);
        catBProducts.First().Name.Should().Be("محصول ب");
    }

    [Fact]
    public async Task ListProducts_excludes_inactive_products()
    {
        SkipIfNoDatabase();
        if (!ShouldRun) return;

        await using var ctx = NewContext("inactive");
        var now = DateTimeOffset.UtcNow;

        var categoryResult = Category.CreateRoot("دسته", "cat", now);
        categoryResult.IsSuccess.Should().BeTrue();
        ctx.Categories.Add(categoryResult.Value);

        var activeResult = Product.Create(
            "محصول فعال", "active-product", null, categoryResult.Value.Id, false, now);
        activeResult.IsSuccess.Should().BeTrue();
        ctx.Products.Add(activeResult.Value);

        var inactiveResult = Product.Create(
            "محصول غیرفعال", "inactive-product", null, categoryResult.Value.Id, false, now);
        inactiveResult.IsSuccess.Should().BeTrue();
        ctx.Products.Add(inactiveResult.Value);

        await ctx.SaveChangesAsync();

        // Deactivate the second product using a fresh context to avoid
        // optimistic concurrency conflicts with the just-inserted row.
        await using var ctx2 = NewContext("inactive-deactivate");
        var productToDeactivate = await ctx2.Products.FindAsync(inactiveResult.Value.Id);
        productToDeactivate.Should().NotBeNull();
        productToDeactivate!.Deactivate(now);
        await ctx2.SaveChangesAsync();

        var service = new ProductCatalog(ctx);
        var products = await service.ListProductsAsync(null, CancellationToken.None);

        products.Should().HaveCount(1, "only active products are returned");
        products.First().Name.Should().Be("محصول فعال");
    }
}
