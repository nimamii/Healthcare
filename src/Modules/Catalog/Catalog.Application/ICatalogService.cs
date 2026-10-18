namespace Healthcare.Modules.Catalog.Application;

using Healthcare.Modules.Catalog.Domain;
using Healthcare.SharedKernel;

public interface ICatalogService
{
    Task<Result<Guid>> CreateCategoryAsync(CreateCategoryCommand command, CancellationToken ct);

    Task<Result<Guid>> CreateProductAsync(CreateProductCommand command, CancellationToken ct);

    Task<Result<Guid>> CreateSkuAsync(CreateSkuCommand command, CancellationToken ct);

    Task<Result> LinkSupplierProductAsync(LinkSupplierProductCommand command, CancellationToken ct);

    Task<Result> SetActiveAsync(SetCatalogActiveCommand command, CancellationToken ct);
}

public sealed record CreateCategoryCommand(
    string Name,
    string Slug,
    Guid? ParentId);

public sealed record CreateProductCommand(
    string Name,
    string Slug,
    string? Description,
    Guid CategoryId,
    bool RequiresPrescription);

public sealed record CreateSkuCommand(
    Guid ProductId,
    string? VariantDescription);

/// <summary>
/// <paramref name="SupplierId"/> is carried as a plain value. Suppliers has
/// not been built yet (Phase 3), so this command cannot validate the id —
/// which is why the module boundary is an interface and not a foreign key.
/// </summary>
public sealed record LinkSupplierProductCommand(
    Guid SkuId,
    Guid SupplierId,
    string SupplierSku,
    decimal? ListPrice,
    string Currency);

public enum CatalogEntity
{
    Category,
    Product,
    Sku,
    SupplierProduct,
}

public sealed record SetCatalogActiveCommand(
    CatalogEntity Entity,
    Guid Id,
    bool IsActive);