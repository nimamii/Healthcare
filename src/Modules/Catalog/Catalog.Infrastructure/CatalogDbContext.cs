using Healthcare.Modules.Catalog.Domain;
using Healthcare.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Healthcare.Modules.Catalog.Infrastructure;

/// <summary>
/// Catalog owns this context and the catalog schema. Other modules read
/// through <c>IProductCatalog</c> rather than querying these tables.
/// </summary>
public class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public const string Schema = "catalog";

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<Sku> Skus => Set<Sku>();

    public DbSet<SupplierProduct> SupplierProducts => Set<SupplierProduct>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema(Schema);

        builder.Entity<Category>(entity =>
        {
            entity.ToTable("categories");
            entity.HasKey(c => c.Id);

            entity.Property(c => c.Name).HasMaxLength(200).IsRequired();
            entity.Property(c => c.Slug).HasMaxLength(200).IsRequired();

            entity.HasIndex(c => c.Slug).IsUnique();

            // Self-reference. ON DELETE RESTRICT is the default, and here it
            // is what stops a category delete from silently removing a whole
            // subtree of products along with it.
            entity.HasOne<Category>()
                  .WithMany()
                  .HasForeignKey(c => c.ParentId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(c => c.ParentId);
            entity.HasIndex(c => new { c.IsActive, c.Name });
        });

        builder.Entity<Product>(entity =>
        {
            entity.ToTable("products");
            entity.HasKey(p => p.Id);

            entity.Property(p => p.Name).HasMaxLength(300).IsRequired();
            entity.Property(p => p.Slug).HasMaxLength(300).IsRequired();
            entity.Property(p => p.Description).HasMaxLength(4000);

            entity.HasIndex(p => p.Slug).IsUnique();
            entity.HasIndex(p => new { p.CategoryId, p.IsActive });

            // A product must live in a category. The FK is real here because
            // both tables are in this schema, and the database is cheaper and
            // more reliable at enforcing it than application code.
            entity.HasOne<Category>()
                  .WithMany()
                  .HasForeignKey(p => p.CategoryId)
                  .OnDelete(DeleteBehavior.Restrict);

            // After HasOne, for the same reason as Sku.ProductId below.
            entity.Property(p => p.CategoryId).HasColumnName("category_id");
        });

        builder.Entity<Sku>(entity =>
        {
            entity.ToTable("skus");
            entity.HasKey(s => s.Id);

            entity.Property(s => s.Code).HasMaxLength(64).IsRequired();
            entity.Property(s => s.VariantDescription).HasMaxLength(300);

            entity.HasIndex(s => s.Code).IsUnique();
            entity.HasIndex(s => new { s.ProductId, s.IsActive });

            // No HasOne<Product>. Sku carries ProductId but no navigation
            // property, and adding one made EF Core create a second
            // relationship to products — a shadow foreign key "product_id1"
            // with no matching column. The id is what callers need; the
            // product name is read through IProductCatalog.
            entity.Property(s => s.ProductId).HasColumnName("product_id");
        });

        builder.Entity<SupplierProduct>(entity =>
        {
            entity.ToTable("supplier_products");
            entity.HasKey(sp => sp.Id);

            entity.Property(sp => sp.SupplierSku).HasMaxLength(128).IsRequired();
            entity.Property(sp => sp.Currency).HasMaxLength(3).IsRequired();


            // numeric(18,4): IRR has no minor unit, USD has two, and some
            // supplier feeds carry unit prices like 1234.5678. Three digits
            // would truncate those; float would introduce rounding drift on
            // money.
            entity.Property(sp => sp.ListPrice).HasPrecision(18, 4);

            // One supplier lists a given SKU once. Re-listing updates the
            // existing row rather than adding a competing one.
            entity.HasIndex(sp => new { sp.SkuId, sp.SupplierId }).IsUnique();

            // No FK on SupplierId. It points at another module's schema, and
            // §13 forbids the database-level coupling that would create.
            entity.HasIndex(sp => sp.SupplierId);

            // No HasOne<Sku>, for the same reason as Sku.ProductId above: a navigation
            // property makes EF Core create a second relationship to the same
            // table, and the second one gets a suffixed name that no column
            // matches.
            entity.Property(sp => sp.SkuId).HasColumnName("sku_id");
        });

        // snake_case columns, per docs/CONVENTIONS.md. Applied across the whole
        // model so a new entity cannot quietly opt out.
        //
        // Must run AFTER the configurations above. HasOne<T>() derives a column
        // name from the relationship and overwrites whatever was set here; when
        // a table is the target of more than one relationship — skus is reached
        // from products and from supplier_products — that derived name is
        // suffixed to "ProductId1", which no column matches. This pass is
        // therefore the last word, not the first.
        foreach (var entity in builder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
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

public sealed class CatalogUnitOfWork(CatalogDbContext db) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}