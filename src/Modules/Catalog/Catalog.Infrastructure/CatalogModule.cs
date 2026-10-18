using Healthcare.Modules.Catalog.Application;
using Healthcare.Modules.Catalog.Contracts;
using Healthcare.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;


namespace Healthcare.Modules.Catalog.Infrastructure;

public sealed class CatalogModule : IModule
{
    public string Name => "catalog";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Default is required. Set it via environment variable (§24).");

        services.AddDbContext<CatalogDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddScoped<CatalogUnitOfWork>();

        // Keyed, not plain. Every module owns a DbContext, so a single
        // IUnitOfWork registration would be overwritten by whichever module
        // registers last — and every write would then commit the wrong
        // context. See IUnitOfWork for what that looked like in practice.
        services.AddKeyedScoped<IUnitOfWork>(UnitOfWorkKeys.Catalog, (sp, _) =>
            sp.GetRequiredService<CatalogUnitOfWork>());

        services.AddScoped<ICatalogService, CatalogService>();
        services.AddScoped<IProductCatalog, ProductCatalog>();

        // Singleton because it is stateless. Its output is random rather than
        // sequential, so two calls on the same instance cannot collide the way
        // a counter would.
        services.AddSingleton<ISkuCodeGenerator, SkuCodeGenerator>();
    }

    /// <summary>
    /// Does nothing on purpose, for the same reason as IdentityModule's: the
    /// schema comes from db/init/001-schemas.sql, which Postgres runs when the
    /// volume is first created.
    /// </summary>
    public void Configure(IServiceProvider services, IHostEnvironment environment)
    {
    }
}

/// <summary>
/// Read side exposed to other modules through Contracts.
/// </summary>
public sealed class ProductCatalog(CatalogDbContext db) : IProductCatalog
{
    public async Task<SkuSummary?> FindSkuByCodeAsync(string code, CancellationToken ct)
    {
        var normalized = code.Trim().ToUpperInvariant();

        // The product name is read in the same query rather than through a
        // navigation property. Sku has no Product navigation — adding one made
        // EF Core create a second relationship to products, producing a shadow
        // foreign key with no matching column.
        //
        // A left join is used rather than a second round trip: the product row
        // must not be able to make a real SKU disappear, and a separate lookup
        // would need its own null handling for a struct that cannot be null.
        var rows = await (
            from sku in db.Skus.AsNoTracking()
            where sku.Code == normalized
            join product in db.Products.AsNoTracking()
                on sku.ProductId equals product.Id into products
            from product in products.DefaultIfEmpty()
            select new SkuSummary(
                sku.Id,
                sku.ProductId,
                sku.Code,
                product == null ? string.Empty : product.Name,
                sku.IsActive))
            .ToListAsync(ct);

        return rows.Count == 0 ? null : rows[0];
    }

    public async Task<ProductSummary?> FindProductAsync(Guid productId, CancellationToken ct) =>
        await db.Products
            .AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => new ProductSummary(
                p.Id, p.Name, p.Slug, p.CategoryId, p.RequiresPrescription, p.IsActive))
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyCollection<ProductSummary>> ListProductsAsync(
        Guid? categoryId,
        CancellationToken ct)
    {
        var query = db.Products
            .AsNoTracking()
            .Where(p => p.IsActive);

        if (categoryId is { } id)
        {
            query = query.Where(p => p.CategoryId == id);
        }

        return await query
            .OrderBy(p => p.Name)
            .Select(p => new ProductSummary(
                p.Id, p.Name, p.Slug, p.CategoryId, p.RequiresPrescription, p.IsActive))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyCollection<SupplierListing>> GetListingsAsync(
        Guid skuId,
        CancellationToken ct)
    {
        var rows = await db.SupplierProducts
            .AsNoTracking()
            .Where(sp => sp.SkuId == skuId && sp.IsActive)
            .OrderBy(sp => sp.ListPrice.HasValue).ThenBy(sp => sp.ListPrice)
            .Select(sp => new SupplierListing(
                sp.Id, sp.SupplierId, sp.SupplierSku, sp.ListPrice, sp.Currency, sp.IsActive))
            .ToListAsync(ct);

        return rows;
    }

    public async Task<bool> RequiresPrescriptionAsync(Guid productId, CancellationToken ct) =>
        await db.Products
            .AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => p.RequiresPrescription)
            .FirstOrDefaultAsync(ct);
}