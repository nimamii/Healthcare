using Healthcare.SharedKernel;

namespace Healthcare.Modules.Catalog.Domain;

/// <summary>
/// A sellable item in the catalogue.
///
/// Deliberately carries no price and no quantity. Those belong to
/// <c>supplier_products</c> and to Inventory, which arrive in later phases.
/// Putting a price here would mean one figure that has to be reconciled
/// against every supplier's own price for the same SKU, and the spec requires
/// those to be independent (§6).
/// </summary>
public sealed class Product
{
    private Product()
    {
        // EF Core.
    }

    private Product(
        Guid id,
        string name,
        string slug,
        string? description,
        Guid categoryId,
        bool requiresPrescription,
        bool isActive,
        DateTimeOffset now)
    {
        Id = id;
        Name = name;
        Slug = slug;
        Description = description;
        CategoryId = categoryId;
        RequiresPrescription = requiresPrescription;
        IsActive = isActive;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Slug { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public Guid CategoryId { get; private set; }

    /// <summary>
    /// Whether buying this item needs a valid prescription. Set by catalog
    /// staff per product, and enforced at checkout in Phase 5.
    ///
    /// It lives on the product rather than on the SKU because prescription
    /// status is a property of what the item is, not of which supplier happens
    /// to stock it. A prescription-only device stays prescription-only no
    /// matter how many sellers list it.
    /// </summary>
    public bool RequiresPrescription { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Result<Product> Create(
        string name,
        string slug,
        string? description,
        Guid categoryId,
        bool requiresPrescription,
        DateTimeOffset now)
    {
        var trimmedName = name.Trim();
        var trimmedSlug = slug.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            return Result<Product>.Failure("product_name_required", "نام محصول الزامی است.");
        }

        if (trimmedName.Length > 300)
        {
            return Result<Product>.Failure("product_name_too_long", "نام محصول نباید بیش از ۳۰۰ کاراکتر باشد.");
        }

        if (string.IsNullOrWhiteSpace(trimmedSlug))
        {
            return Result<Product>.Failure("product_slug_required", "شناسه یکتا (slug) الزامی است.");
        }

        if (categoryId == Guid.Empty)
        {
            return Result<Product>.Failure("product_category_required", "دسته‌بندی محصول الزامی است.");
        }

        return Result<Product>.Success(new Product(
            Guid.NewGuid(),
            trimmedName,
            trimmedSlug,
            string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            categoryId,
            requiresPrescription,
            isActive: true,
            now));
    }

    public void UpdateDetails(
        string name,
        string slug,
        string? description,
        Guid categoryId,
        bool requiresPrescription,
        DateTimeOffset now)
    {
        Name = name.Trim();
        Slug = slug.Trim().ToLowerInvariant();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        CategoryId = categoryId;
        RequiresPrescription = requiresPrescription;
        UpdatedAt = now;
    }

    public void SetRequiresPrescription(bool required, DateTimeOffset now)
    {
        RequiresPrescription = required;
        UpdatedAt = now;
    }

    public void Deactivate(DateTimeOffset now)
    {
        IsActive = false;
        UpdatedAt = now;
    }

    public void Reactivate(DateTimeOffset now)
    {
        IsActive = true;
        UpdatedAt = now;
    }
}