using Healthcare.Modules.Catalog.Infrastructure;
using Microsoft.EntityFrameworkCore;

// Print what EF Core's model actually contains.
//
// Guessing at the column name produced three wrong fixes in a row. This asks
// the model directly, so the answer is the one EF will use at runtime rather
// than the one the configuration appears to request.

var options = new DbContextOptionsBuilder<CatalogDbContext>()
    .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
    .Options;

using var db = new CatalogDbContext(options);

Console.WriteLine("Tables and columns as EF Core sees them:");
Console.WriteLine();

foreach (var entityType in db.Model.GetEntityTypes().OrderBy(e => e.GetTableName()))
{
    var table = entityType.GetTableName();
    var schema = entityType.GetSchema();

    Console.WriteLine($"  {schema}.{table}");

    foreach (var property in entityType.GetProperties().OrderBy(p => p.GetColumnName()))
    {
        var column = property.GetColumnName();
        var isKey = property.IsKey();
        var maxLength = property.GetMaxLength();

        Console.WriteLine(
            $"    {column,-24} clr={property.Name,-24} " +
            $"{(isKey ? "KEY" : "    ")} {(maxLength > 0 ? $"maxlen={maxLength}" : "")}");
    }

    Console.WriteLine();
}

Console.WriteLine("Foreign keys:");

foreach (var entityType in db.Model.GetEntityTypes())
{
    foreach (var fk in entityType.GetForeignKeys())
    {
        var principal = fk.PrincipalEntityType.GetTableName();

        var properties = string.Join(", ", fk.Properties.Select(p => p.GetColumnName()));
        var principalProperties = string.Join(", ", fk.PrincipalKey.Properties.Select(p => p.GetColumnName()));

        Console.WriteLine(
            $"  {entityType.GetTableName()}({properties}) " +
            $"-> {principal}({principalProperties})");
    }
}
