using System.Security.Cryptography;
using Healthcare.Modules.Catalog.Application;
using Healthcare.Modules.Catalog.Domain;
using Healthcare.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Healthcare.Modules.Catalog.Infrastructure;

/// <summary>
/// Generates our own SKU codes.
///
/// The scheme lives here rather than in the domain because it is a deployment
/// concern: what the codes look like is not a business rule, and changing it
/// must not require touching the entities that validate them.
/// </summary>
public interface ISkuCodeGenerator
{
    string Generate();
}

public sealed class SkuCodeGenerator : ISkuCodeGenerator
{
    /// <summary>
    /// 12 characters from an unambiguous alphabet: no O/0, no I/1/L.
    ///
    /// These codes get read aloud to support staff and typed by hand into
    /// phone orders, so "O" versus "0" is a real source of errors rather than
    /// a theoretical one.
    /// </summary>
    private const string Alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

    public string Generate()
    {
        var chars = new char[12];

        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return $"SKU-{new string(chars)}";
    }
}

public sealed class CatalogService(
    CatalogDbContext db,
    [FromKeyedServices(UnitOfWorkKeys.Catalog)] IUnitOfWork unitOfWork,
    ISkuCodeGenerator codeGenerator,
    TimeProvider clock) : ICatalogService
{
    public async Task<Result<Guid>> CreateCategoryAsync(
        CreateCategoryCommand command,
        CancellationToken ct)
    {
        // Depth is read from the parent row, never taken from the request. A
        // caller-supplied depth would let a crafted request place a category at
        // level 99, and the stored depth would then be wrong for every
        // descendant.
        var parentDepth = 0;

        if (command.ParentId is { } parentId)
        {
            var parent = await db.Categories
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == parentId, ct);

            if (parent is null)
            {
                return Result<Guid>.Failure("category_parent_not_found", "دسته‌بندی والد یافت نشد.");
            }

            parentDepth = parent.Depth;
        }

        // Two factories, because the two cases cannot be confused. With a single
        // method taking an optional parentId, a caller could pass parentDepth
        // alongside a null parent and the depth would silently be 1.
        var result = command.ParentId is null
            ? Category.CreateRoot(command.Name, command.Slug, clock.GetUtcNow())
            : Category.Create(command.Name, command.Slug, parentDepth, clock.GetUtcNow());

        if (result.IsFailure)
        {
            return Result<Guid>.Failure(result.ErrorCode!, result.ErrorMessage!);
        }

        var category = result.Value;

        if (await db.Categories.AnyAsync(c => c.Slug == category.Slug, ct))
        {
            return Result<Guid>.Failure("category_slug_taken", "این شناسه یکتا قبلاً استفاده شده است.");
        }

        db.Categories.Add(category);
        await unitOfWork.SaveChangesAsync(ct);

        return Result<Guid>.Success(category.Id);
    }

    public async Task<Result<Guid>> CreateProductAsync(
        CreateProductCommand command,
        CancellationToken ct)
    {
        var category = await db.Categories
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == command.CategoryId, ct);

        if (category is null)
        {
            return Result<Guid>.Failure("product_category_not_found", "دسته‌بندی یافت نشد.");
        }

        if (!category.IsActive)
        {
            return Result<Guid>.Failure(
                "product_category_inactive",
                "محصول نمی‌تواند در دسته‌بندی غیرفعال قرار گیرد.");
        }

        var result = Product.Create(
            command.Name,
            command.Slug,
            command.Description,
            command.CategoryId,
            command.RequiresPrescription,
            clock.GetUtcNow());

        if (result.IsFailure)
        {
            return Result<Guid>.Failure(result.ErrorCode!, result.ErrorMessage!);
        }

        var product = result.Value;

        if (await db.Products.AnyAsync(p => p.Slug == product.Slug, ct))
        {
            return Result<Guid>.Failure("product_slug_taken", "این شناسه یکتا قبلاً استفاده شده است.");
        }

        db.Products.Add(product);
        await unitOfWork.SaveChangesAsync(ct);

        return Result<Guid>.Success(product.Id);
    }

    public async Task<Result<Guid>> CreateSkuAsync(CreateSkuCommand command, CancellationToken ct)
    {
        var product = await db.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == command.ProductId, ct);

        if (product is null)
        {
            return Result<Guid>.Failure("sku_product_not_found", "محصول یافت نشد.");
        }

        // Retry on the astronomically unlikely code collision rather than
        // failing a legitimate request. Twelve characters is enough that a
        // repeat means something is wrong with the generator.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var result = Sku.Create(
                command.ProductId,
                command.VariantDescription,
                clock.GetUtcNow(),
                codeGenerator.Generate);

            if (result.IsFailure)
            {
                return Result<Guid>.Failure(result.ErrorCode!, result.ErrorMessage!);
            }

            var sku = result.Value;

            if (await db.Skus.AnyAsync(s => s.Code == sku.Code, ct))
            {
                continue;
            }

            db.Skus.Add(sku);
            await unitOfWork.SaveChangesAsync(ct);

            return Result<Guid>.Success(sku.Id);
        }

        return Result<Guid>.Failure(
            "sku_code_generation_failed",
            "تولید کد SKU پس از چند تلاش ناموفق بود.");
    }

    public async Task<Result> LinkSupplierProductAsync(
        LinkSupplierProductCommand command,
        CancellationToken ct)
    {
        var sku = await db.Skus
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == command.SkuId, ct);

        if (sku is null)
        {
            return Result.Failure("supplier_product_sku_not_found", "SKU یافت نشد.");
        }

        if (!sku.IsActive)
        {
            return Result.Failure("supplier_product_sku_inactive", "SKU غیرفعال است.");
        }

        var result = SupplierProduct.Create(
            command.SkuId,
            command.SupplierId,
            command.SupplierSku,
            command.ListPrice,
            command.Currency,
            clock.GetUtcNow());

        if (result.IsFailure)
        {
            return Result.Failure(result.ErrorCode!, result.ErrorMessage!);
        }

        var listing = result.Value;

        var existing = await db.SupplierProducts
            .FirstOrDefaultAsync(sp => sp.SkuId == command.SkuId && sp.SupplierId == command.SupplierId, ct);

        if (existing is not null)
        {
            // Re-listing updates rather than duplicates: the unique index on
            // (sku_id, supplier_id) means a second insert would throw.
            existing.UpdateDetails(
                listing.SupplierSku,
                listing.ListPrice,
                listing.Currency,
                clock.GetUtcNow());
        }
        else
        {
            db.SupplierProducts.Add(listing);
        }

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<Result> SetActiveAsync(SetCatalogActiveCommand command, CancellationToken ct)
    {
        var now = clock.GetUtcNow();

        switch (command.Entity)
        {
            case CatalogEntity.Category:
            {
                var entity = await db.Categories.FirstOrDefaultAsync(c => c.Id == command.Id, ct);
                if (entity is null) return Result.Failure("category_not_found", "دسته‌بندی یافت نشد.");

                if (command.IsActive) entity.Reactivate(now); else entity.Deactivate(now);
                break;
            }

            case CatalogEntity.Product:
            {
                var entity = await db.Products.FirstOrDefaultAsync(p => p.Id == command.Id, ct);
                if (entity is null) return Result.Failure("product_not_found", "محصول یافت نشد.");

                if (command.IsActive) entity.Reactivate(now); else entity.Deactivate(now);
                break;
            }

            case CatalogEntity.Sku:
            {
                var entity = await db.Skus.FirstOrDefaultAsync(s => s.Id == command.Id, ct);
                if (entity is null) return Result.Failure("sku_not_found", "SKU یافت نشد.");

                if (command.IsActive) entity.Reactivate(now); else entity.Deactivate(now);
                break;
            }

            case CatalogEntity.SupplierProduct:
            {
                var entity = await db.SupplierProducts.FirstOrDefaultAsync(sp => sp.Id == command.Id, ct);
                if (entity is null) return Result.Failure("supplier_product_not_found", "یافت نشد.");

                if (command.IsActive) entity.Reactivate(now); else entity.Deactivate(now);
                break;
            }

            default:
                return Result.Failure("unknown_entity", "نوع موجودیت نامعتبر است.");
        }

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}