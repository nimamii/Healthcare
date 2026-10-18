using Healthcare.Modules.Inventory.Domain;
using Healthcare.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Healthcare.Modules.Inventory.Infrastructure;

/// <summary>
/// Inventory owns this context and the inventory schema.
/// </summary>
public class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public const string Schema = "inventory";

    public DbSet<StockItem> StockItems => Set<StockItem>();

    public DbSet<Reservation> Reservations => Set<Reservation>();

    /// <summary>
    /// Advances the concurrency token on every stock write.
    ///
    /// The token is written here rather than at each call site because a
    /// forgotten increment disables concurrency control silently: the version
    /// stays at 1, the row filter in UPDATE ... WHERE version = n never
    /// rejects anything, and two buyers both take the last unit. That is
    /// overselling, and nothing in the system looks broken (§39).
    ///
    /// EntityEntry.Entity is get-only, so the token is set through
    /// CurrentValues rather than by assigning a new entity back.
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries<StockItem>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.CurrentValues[nameof(StockItem.Version)] = entry.Entity.Version + 1;
                entry.CurrentValues[nameof(StockItem.UpdatedAt)] = now;
            }
        }

        return await base.SaveChangesAsync(ct);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema(Schema);

        // Named after the entities, so the loop below cannot clobber them.
        builder.Entity<StockItem>(entity =>
        {
            entity.ToTable("stock_items");
            entity.HasKey(s => s.Id);

            entity.Property(s => s.Version).IsConcurrencyToken();

            entity.HasIndex(s => new { s.SkuId, s.SupplierId }).IsUnique();
            entity.HasIndex(s => s.SkuId);
        });

        builder.Entity<Reservation>(entity =>
        {
            entity.ToTable("reservations");
            entity.HasKey(r => r.Id);

            // Stored as an integer, matching the SQL check constraint, rather
            // than letting EF map the enum to a string. The database can then
            // reject an invalid value, which a varchar mapping would not.
            entity.Property(r => r.Status).HasConversion<int>();

            // One reservation per (order, sku). This is what makes a retried
            // request harmless instead of double-holding stock (§6).
            entity.HasIndex(r => new { r.OrderId, r.SkuId }).IsUnique();

            // Partial index matching the SQL: the sweep only ever asks about
            // active rows.
            entity.HasIndex(r => r.ExpiresAt).HasFilter("status = 0");
        });

        // snake_case columns, run last so it is the final word. See the note in
        // CatalogDbContext for why it cannot come first.
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }
        }
    }

    private static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name) || !name.Any(char.IsUpper))
        {
            return name;
        }

        var builder = new System.Text.StringBuilder(name.Length + 4);

        for (var i = 0; i < name.Length; i++)
        {
            var current = name[i];

            if (i > 0 && char.IsUpper(current) && name[i - 1] != '_')
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(current));
        }

        return builder.ToString();
    }
}

public sealed class InventoryUnitOfWork(InventoryDbContext db) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}