using FluentAssertions;
using Healthcare.Modules.Inventory.Domain;
using Healthcare.Modules.Inventory.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Healthcare.Tests.Inventory;

/// <summary>
/// Pins the EF Core model against db/init/001-schemas.sql.
///
/// Two classes of mistake are caught here, both of which compile cleanly and
/// fail only at runtime:
///
///   - a computed property mapped to a column that does not exist, which
///     makes the context fail to start at all;
///   - a second relationship to the same table, which EF Core disambiguates
///     by suffixing the column name ("product_id1") — the bug that cost an
///     afternoon in Phase 2.
/// </summary>
public sealed class ModelShapeTests
{
    private static InventoryDbContext CreateContext()
    {
        // Never opened; building the model does not connect.
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql("Host=localhost;Database=never_used;Username=never_used;Password=never_used")
            .Options;

        return new InventoryDbContext(options);
    }

    [Fact]
    public void No_column_is_generated_with_a_numeric_suffix()
    {
        using var db = CreateContext();

        var offenders = db.Model
            .GetEntityTypes()
            .SelectMany(e => e.GetProperties())
            .Where(p => char.IsDigit(p.Name[^1]))
            .Select(p => $"{p.DeclaringType.GetTableName()}.{p.GetColumnName()}")
            .ToList();

        offenders.Should().BeEmpty(
            "a suffixed column means EF Core created a second relationship " +
            "to the same table, and no column matches it");
    }

    [Fact]
    public void No_entity_defines_more_than_one_relationship_to_the_same_table()
    {
        using var db = CreateContext();

        var duplicates = db.Model
            .GetEntityTypes()
            .SelectMany(e => e.GetForeignKeys()
                .GroupBy(fk => fk.PrincipalEntityType.GetTableName())
                .Where(g => g.Count() > 1)
                .Select(g => $"{e.GetTableName()} -> {g.Key}"))
            .ToList();

        duplicates.Should().BeEmpty(
            "two foreign keys to one table is what makes EF Core suffix a column");
    }

    [Fact]
    public void The_model_produces_exactly_the_columns_the_schema_defines()
    {
        var expected = new Dictionary<string, HashSet<string>>
        {
            ["stock_items"] =
            [
                "id", "sku_id", "supplier_id",
                "available_quantity", "reserved_quantity", "version",
                "created_at", "updated_at",
            ],
            ["reservations"] =
            [
                "id", "order_id", "sku_id", "quantity", "status",
                "expires_at", "created_at", "resolved_at",
            ],
        };

        using var db = CreateContext();

        foreach (var (table, expectedColumns) in expected)
        {
            var entityType = db.Model.GetEntityTypes()
                .FirstOrDefault(e => e.GetTableName() == table);

            entityType.Should().NotBeNull($"{table} is missing from the model");

            var actual = entityType!.GetProperties()
                .Select(p => p.GetColumnName())
                .ToHashSet();

            actual.Should().BeEquivalentTo(
                expectedColumns,
                $"{table} must map to exactly the columns db/init creates — " +
                "an extra one is a property EF mapped that has no column");
        }
    }

    [Fact]
    public void StockItem_maps_no_computed_property_to_a_column()
    {
        // TotalQuantity is derived. Without [NotMapped] EF creates a
        // total_quantity column that the schema does not have.
        using var db = CreateContext();

        var stockItem = db.Model.FindEntityType(
            "Healthcare.Modules.Inventory.Domain.StockItem")!;

        var property = stockItem.FindProperty(nameof(StockItem.TotalQuantity));

        property.Should().BeNull(
            "TotalQuantity is computed from two columns and must not be stored");
    }

    [Fact]
    public void Version_is_a_concurrency_token()
    {
        // Without this the database accepts concurrent writes silently, and
        // two reservations for the last unit both succeed.
        using var db = CreateContext();

        var stockItem = db.Model.FindEntityType(
            "Healthcare.Modules.Inventory.Domain.StockItem")!;

        var version = stockItem.FindProperty(nameof(StockItem.Version));

        version.Should().NotBeNull();
        version!.IsConcurrencyToken.Should().BeTrue(
            "optimistic concurrency needs the column declared as a token");
    }

    [Fact]
    public void Reservation_status_is_stored_as_an_integer()
    {
        // The schema has a CHECK on integer values. A string mapping would
        // write 'Active' into an integer column and fail at insert time.
        using var db = CreateContext();

        var reservation = db.Model.FindEntityType(
            "Healthcare.Modules.Inventory.Domain.Reservation")!;

        var status = reservation.FindProperty(nameof(Reservation.Status))!;

        status.GetColumnType().Should().Be("integer");
    }

    [Fact]
    public void No_entity_declares_a_navigation_property()
    {
        // StockItem and Reservation reference Catalog, Suppliers and Orders by
        // id. A navigation to any of them makes EF treat the id and the
        // navigation as two separate relationships.
        using var db = CreateContext();

        var navigations = db.Model
            .GetEntityTypes()
            .SelectMany(e => e.GetNavigations()
                .Select(n => $"{e.GetTableName()}.{n.Name}"))
            .ToList();

        navigations.Should().BeEmpty(
            "navigation properties create a second relationship to the same " +
            "table, which EF Core disambiguates with a suffixed column");
    }
}