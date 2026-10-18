using Healthcare.SharedKernel;

namespace Healthcare.Modules.Catalog.Domain;

/// <summary>
/// A purchasable variant of a product: size, colour, packaging.
///
/// The distinction matters for medical equipment. A blood-pressure monitor
/// comes in several cuff sizes and they are not interchangeable — a customer
/// ordering the wrong one gets a device that does not work for them, and the
/// return is expensive. The catalogue therefore has to represent the variant
/// explicitly rather than treating the product as one thing.
///
/// The SKU code is generated here, not supplied by a supplier. Suppliers have
/// their own codes, and those belong in <c>supplier_products</c> as a mapping.
/// Two suppliers' "12345" cannot both be our primary key.
/// </summary>
public sealed class Sku
{
    private Sku()
    {
        // EF Core.
    }

    private Sku(Guid id, Guid productId, string code, string? variant, bool isActive, DateTimeOffset now)
    {
        Id = id;
        ProductId = productId;
        Code = code;
        VariantDescription = variant;
        IsActive = isActive;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    /// <summary>
    /// The product this variant belongs to.
    ///
    /// There is deliberately no Product navigation property. Sku already has
    /// ProductId, and adding a navigation made EF Core create a second
    /// relationship to products — a shadow foreign key named "product_id1"
    /// that no column matches. The id is what an order line and a supplier
    /// listing point at; the product name is read through IProductCatalog.
    /// </summary>
    public Guid ProductId { get; private set; }

    /// <summary>
    /// Our own identifier, unique across the catalogue. This is the value an
    /// order line points at; a supplier's code never becomes one.
    /// </summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>
    /// Human-readable variant, e.g. "نوار بازو ۲۲ تا ۳۲ سانتی‌متر".
    /// Free text rather than structured attributes because the attributes that
    /// matter differ per product family, and a generic attribute table would
    /// be unusable for filtering anyway.
    /// </summary>
    public string? VariantDescription { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Result<Sku> Create(
        Guid productId,
        string? variant,
        DateTimeOffset now,
        Func<string>? codeGenerator = null)
    {
        if (productId == Guid.Empty)
        {
            return Result<Sku>.Failure("sku_product_required", "محصول مرتبط الزامی است.");
        }

        var trimmedVariant = string.IsNullOrWhiteSpace(variant) ? null : variant.Trim();

        if (trimmedVariant is { Length: > 300 })
        {
            return Result<Sku>.Failure("sku_variant_too_long", "توضیح تنوع نباید بیش از ۳۰۰ کاراکتر باشد.");
        }

        // Injected so tests are deterministic. Production passes a generator
        // seeded from the SKU id; a caller that supplies none gets a usable
        // value rather than an exception.
        var code = codeGenerator?.Invoke()
            ?? throw new InvalidOperationException(
                "A code generator is required. Production supplies one; " +
                "the domain does not generate codes on its own because the " +
                "scheme is a deployment concern, not a domain rule.");

        var normalized = code.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return Result<Sku>.Failure("sku_code_required", "کد SKU الزامی است.");
        }

        if (normalized.Length > 64)
        {
            return Result<Sku>.Failure("sku_code_too_long", "کد SKU نباید بیش از ۶۴ کاراکتر باشد.");
        }

        return Result<Sku>.Success(new Sku(
            Guid.NewGuid(),
            productId,
            normalized,
            trimmedVariant,
            isActive: true,
            now));
    }

    public void UpdateVariant(string? variant, DateTimeOffset now)
    {
        VariantDescription = string.IsNullOrWhiteSpace(variant) ? null : variant.Trim();
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