namespace Healthcare.Tests.Catalog;

using FluentAssertions;
using Healthcare.Modules.Catalog.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>
/// Pins the shape of the EF Core model against the SQL schema.
///
/// This test exists because of a bug that only appeared at runtime: when an
/// entity carries both a foreign-key property and a navigation property,
/// EF Core treats them as two relationships to the same table and suffixes the
/// second one's column name — "product_id1" — which no column matches. The
/// build succeeded, the integration flow worked up to that row, and the
/// insert failed with an error that named a column nobody wrote.
///
/// A model test catches it before a request ever reaches the database, which
/// is where a schema mismatch is cheapest to fix.
/// </summary>
public sealed class ModelShapeTests
{
    private static CatalogDbContext CreateContext()
    {
        // The connection string is never used: building the model does not
        // open a connection.
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql("Host=localhost;Database=never_used;Username=never_used;Password=never_used")
            .Options;

        return new CatalogDbContext(options);
    }

    [Fact]
    public void No_column_is_generated_with_a_numeric_suffix()
    {
        using var db = CreateContext();

        var offenders = db.Model
            .GetEntityTypes()
            .SelectMany(e => e.GetProperties())
            .Where(p => char.IsDigit(p.Name[^1]) && char.IsLower(p.GetColumnName()[^1]))
            .Select(p => $"{p.DeclaringType.GetTableName()}.{p.GetColumnName()}")
            .ToList();

        offenders.Should().BeEmpty(
            "a suffixed column means EF Core created a second relationship " +
            "to the same table, and the extra column has no matching schema");
    }

    [Fact]
    public void Each_fk_column_name_matches_the_schema()
    {
        using var db = CreateContext();

        var expected = new Dictionary<string, string>
        {
            ["Category.ParentId"] = "parent_id",
            ["Product.CategoryId"] = "category_id",
            ["Sku.ProductId"] = "product_id",
            ["SupplierProduct.SkuId"] = "sku_id",
        };

        foreach (var (property, column) in expected)
        {
            var entityType = db.Model.FindEntityType(
                "Healthcare.Modules.Catalog.Domain." + property.Split('.')[0]);

            entityType.Should().NotBeNull($"entity {property.Split('.')[0]} is not in the model");

            var clrName = property.Split('.')[1];
            var actual = entityType!
                .FindProperty(clrName)!
                .GetColumnName();

            actual.Should().Be(column, $"{property} must map to the column db/init names");
        }
    }

    [Fact]
    public void Each_table_has_the_columns_the_sql_file_creates()
    {
        using var db = CreateContext();

        var schema = new Dictionary<string, HashSet<string>>
        {
            ["categories"] = new()
            {
                "id", "name", "slug", "parent_id", "depth",
                "is_active", "created_at", "updated_at",
            },
            ["products"] = new()
            {
                "id", "name", "slug", "description", "category_id",
                "requires_prescription", "is_active", "created_at", "updated_at",
            },
            ["skus"] = new()
            {
                "id", "product_id", "code", "variant_description",
                "is_active", "created_at", "updated_at",
            },
            ["supplier_products"] = new()
            {
                "id", "sku_id", "supplier_id", "supplier_sku",
                "list_price", "currency", "is_active", "created_at", "updated_at",
            },
        };

        foreach (var (table, expectedColumns) in schema)
        {
            var entityType = db.Model.GetEntityTypes()
                .First(e => e.GetTableName() == table);

            var actual = entityType.GetProperties()
                .Select(p => p.GetColumnName())
                .ToHashSet();

            // Every expected column must exist. Columns the model adds but the
            // SQL file does not are caught by the count check below, which is
            // the case that produced "product_id1".
            foreach (var column in expectedColumns)
            {
                actual.Should().Contain(
                    column,
                    $"{table} needs {column} to match db/init/001-schemas.sql");
            }

            actual.Should().HaveCount(
                expectedColumns.Count,
                $"{table} should have exactly these columns — an extra one " +
                "means a relationship created a column no schema defines");
        }
    }

    [Fact]
    public void Every_foreign_key_maps_to_a_real_column()
    {
        using var db = CreateContext();

        var unknown = db.Model
            .GetEntityTypes()
            .SelectMany(e => e.GetForeignKeys())
            .SelectMany(fk => fk.Properties)
            .Where(p => p.GetColumnName() is { } column && char.IsDigit(column[^1]))
            .Select(p => p.GetColumnName());

        unknown.Should().BeEmpty(
            "a foreign key pointing at a suffixed column will fail at insert " +
            "time with a name error");
    }

    [Fact]
    public void Sku_has_no_navigation_property()
    {
        // The navigation caused the second relationship in the first place.
        // Asserting its absence keeps someone from re-adding it for query
        // convenience and reintroducing the bug.
        using var db = CreateContext();

        var skus = db.Model.FindEntityType("Healthcare.Modules.Catalog.Domain.Sku")!;

        var navigations = skus.GetNavigations().ToList();

        navigations.Should().BeEmpty(
            "Sku must stay navigation-free: any navigation to products " +
            "creates a second relationship and a suffixed column");
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
                .Select(g => $"{e.GetTableName()} -> {g.Key} ({g.Count()} times)"))
            .ToList();

        duplicates.Should().BeEmpty(
            "two foreign keys to one table is how EF Core decides a column " +
            "needs a numeric suffix");
    }
}