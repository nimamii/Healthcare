using Healthcare.SharedKernel;

namespace Healthcare.Modules.Catalog.Domain;

/// <summary>
/// Links one of our SKUs to one supplier's version of it.
///
/// Created in this phase on purpose, though Suppliers and Inventory arrive in
/// Phase 3. The mapping has to exist before any price or stock does, because
/// the alternative is retrofitting it later with real rows to migrate. An
/// empty table costs nothing and removes that work.
///
/// <see cref="SupplierId"/> is a plain Guid with no foreign key. Suppliers
/// lives in another schema and another module, and the spec forbids
/// cross-module joins (§13) — the referential check happens through
/// <c>Suppliers.Contracts</c> instead. The same applies to
/// <c>SkuId</c>: that one points inside this schema and does get a real
/// constraint, because nothing prevents the database from enforcing it here.
/// </summary>
public sealed class SupplierProduct
{
    private SupplierProduct()
    {
        // EF Core.
    }

    private SupplierProduct(
        Guid id,
        Guid skuId,
        Guid supplierId,
        string supplierSku,
        decimal? listPrice,
        string currency,
        bool isActive,
        DateTimeOffset now)
    {
        Id = id;
        SkuId = skuId;
        SupplierId = supplierId;
        SupplierSku = supplierSku;
        ListPrice = listPrice;
        Currency = currency;
        IsActive = isActive;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid SkuId { get; private set; }

    /// <summary>
    /// No foreign key: the Suppliers module owns this reference. Adding one
    /// would create a hard database dependency between two modules that must
    /// stay separable.
    /// </summary>
    public Guid SupplierId { get; private set; }

    /// <summary>
    /// The supplier's own code for this item. Never used as a key on our side:
    /// two suppliers can both call something "12345", and one supplier can
    /// change their code at any time.
    /// </summary>
    public string SupplierSku { get; private set; } = string.Empty;

    /// <summary>
    /// The supplier's advertised price. Null means not published — distinct
    /// from zero, which would be a free item and is never a real answer.
    ///
    /// The customer's actual price comes from Inventory in Phase 3, which is
    /// the live figure. This is what the catalogue page shows when nothing
    /// else is known.
    /// </summary>
    public decimal? ListPrice { get; private set; }

    /// <summary>ISO 4217. Fixed rather than derived, because a supplier's feed will disagree.</summary>
    public string Currency { get; private set; } = "IRR";

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Result<SupplierProduct> Create(
        Guid skuId,
        Guid supplierId,
        string supplierSku,
        decimal? listPrice,
        string currency,
        DateTimeOffset now)
    {
        if (skuId == Guid.Empty)
        {
            return Result<SupplierProduct>.Failure("supplier_product_sku_required", "SKU الزامی است.");
        }

        if (supplierId == Guid.Empty)
        {
            return Result<SupplierProduct>.Failure(
                "supplier_product_supplier_required",
                "تأمین‌کننده الزامی است.");
        }

        var trimmedCode = supplierSku?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmedCode))
        {
            return Result<SupplierProduct>.Failure(
                "supplier_product_code_required",
                "کد تأمین‌کننده الزامی است.");
        }

        if (trimmedCode.Length > 128)
        {
            return Result<SupplierProduct>.Failure(
                "supplier_product_code_too_long",
                "کد تأمین‌کننده نباید بیش از ۱۲۸ کاراکتر باشد.");
        }

        if (listPrice is < 0)
        {
            return Result<SupplierProduct>.Failure(
                "supplier_product_negative_price",
                "قیمت نمی‌تواند منفی باشد.");
        }

        if (listPrice is 0)
        {
            return Result<SupplierProduct>.Failure(
                "supplier_product_zero_price",
                "قیمت صفر معتبر نیست؛ اگر قیمت اعلام نشده، مقدار را خالی بگذارید.");
        }

        // Two decimal places is wrong for IRR, which has none. The column is
        // numeric(18,4), so the domain only rejects precision it cannot store.
        if (listPrice is { } price && decimal.Round(price, 4) != price)
        {
            return Result<SupplierProduct>.Failure(
                "supplier_product_price_precision",
                "قیمت نمی‌تواند بیش از ۴ رقم اعشار داشته باشد.");
        }

        var normalizedCurrency = string.IsNullOrWhiteSpace(currency)
            ? "IRR"
            : currency.Trim().ToUpperInvariant();

        if (normalizedCurrency.Length != 3)
        {
            return Result<SupplierProduct>.Failure(
                "supplier_product_currency_invalid",
                "کد ارز باید سه حرف باشد (مثل IRR یا USD).");
        }

        return Result<SupplierProduct>.Success(new SupplierProduct(
            Guid.NewGuid(),
            skuId,
            supplierId,
            trimmedCode,
            listPrice,
            normalizedCurrency,
            isActive: true,
            now));
    }

    public void UpdateDetails(
        string supplierSku,
        decimal? listPrice,
        string currency,
        DateTimeOffset now)
    {
        SupplierSku = supplierSku.Trim();
        ListPrice = listPrice;
        Currency = currency.Trim().ToUpperInvariant();
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