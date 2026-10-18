namespace Healthcare.Modules.Catalog.Contracts;

/// <summary>
/// How other modules read the catalogue.
///
/// The write side is deliberately not exposed here: Inventory and Orders need
/// to look things up, and nothing outside this module should be able to create
/// or edit catalogue entries (§13).
/// </summary>
public interface IProductCatalog
{
    Task<SkuSummary?> FindSkuByCodeAsync(string code, CancellationToken ct);

    Task<ProductSummary?> FindProductAsync(Guid productId, CancellationToken ct);

    /// <summary>
    /// Lists all active products, optionally filtered by category.
    ///
    /// Used by the marketplace frontend to render the catalogue. Returns only
    /// active products; inactive ones are administrative state, not catalogue
    /// entries.
    /// </summary>
    Task<IReadOnlyCollection<ProductSummary>> ListProductsAsync(
        Guid? categoryId,
        CancellationToken ct);

    /// <summary>
    /// Listings for one SKU, across suppliers.
    ///
    /// Order and Fulfillment both need this. It returns the supplier's own code
    /// and advertised price, never a quantity — that belongs to Inventory,
    /// which the spec requires to stay the single source of stock truth (§6).
    /// </summary>
    Task<IReadOnlyCollection<SupplierListing>> GetListingsAsync(Guid skuId, CancellationToken ct);

    /// <summary>
    /// Whether a product requires a prescription. Checked at checkout in
    /// Phase 5; exposed now so the flag has a defined meaning before it is
    /// relied upon.
    /// </summary>
    Task<bool> RequiresPrescriptionAsync(Guid productId, CancellationToken ct);
}

public readonly record struct SkuSummary(
    Guid Id,
    Guid ProductId,
    string Code,
    string ProductName,
    bool IsActive);

public readonly record struct ProductSummary(
    Guid Id,
    string Name,
    string Slug,
    Guid CategoryId,
    bool RequiresPrescription,
    bool IsActive);

public readonly record struct SupplierListing(
    Guid Id,
    Guid SupplierId,
    string SupplierSku,
    decimal? ListPrice,
    string Currency,
    bool IsActive);