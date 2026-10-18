using System.ComponentModel.DataAnnotations.Schema;
using Healthcare.SharedKernel;

namespace Healthcare.Modules.Inventory.Domain;

/// <summary>
/// Stock for one SKU from one supplier.
///
/// Concurrency is optimistic: every write states the version it read, and the
/// database refuses the write if someone else got there first. That suits a
/// web deployment because a request holds no lock while a user thinks — the
/// statement either matches the expected version or changes nothing.
///
/// The alternative, SELECT ... FOR UPDATE, holds a transaction open for the
/// duration and queues under contention. With three people on this project and
/// a web frontend, optimistic is the better trade.
/// </summary>
public sealed class StockItem
{
    private StockItem()
    {
        // EF Core.
    }

    private StockItem(
        Guid id,
        Guid skuId,
        Guid supplierId,
        int availableQuantity,
        int reservedQuantity,
        DateTimeOffset now)
    {
        Id = id;
        SkuId = skuId;
        SupplierId = supplierId;
        AvailableQuantity = availableQuantity;
        ReservedQuantity = reservedQuantity;
        Version = 1;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    /// <summary>
    /// No foreign key: Catalog owns this reference and lives in another
    /// schema. The lookup goes through Catalog.Contracts, per the rule against
    /// cross-module database joins (§13).
    /// </summary>
    public Guid SkuId { get; private set; }

    /// <summary>Also cross-module; Suppliers arrives in this same phase.</summary>
    public Guid SupplierId { get; private set; }

    /// <summary>Free stock: not reserved, not yet shipped.</summary>
    public int AvailableQuantity { get; private set; }

    /// <summary>
    /// Held for orders that have not been paid or cancelled.
    ///
    /// Kept separate from available rather than subtracted from it, because
    /// releasing a reservation has to add the quantity back without knowing
    /// whether anything else moved in the meantime. One counter cannot express
    /// both states.
    /// </summary>
    public int ReservedQuantity { get; private set; }

    /// <summary>
    /// Incremented on every successful write. The update statement carries the
    /// value it read; a mismatch means the write is rejected and retried.
    /// </summary>
    public long Version { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Total physically held, for reporting.
    ///
    /// [NotMapped] because it is derived: without it EF Core tries to create
    /// a total_quantity column that does not exist in the schema, and the
    /// failure appears at startup as a migration mismatch rather than as
    /// "you forgot to exclude a computed property".
    /// </summary>
    [NotMapped]
    public int TotalQuantity => AvailableQuantity + ReservedQuantity;

    public static Result<StockItem> Create(
        Guid skuId,
        Guid supplierId,
        int initialQuantity,
        DateTimeOffset now)
    {
        if (skuId == Guid.Empty)
        {
            return Result<StockItem>.Failure("stock_sku_required", "SKU الزامی است.");
        }

        if (supplierId == Guid.Empty)
        {
            return Result<StockItem>.Failure("stock_supplier_required", "تأمین‌کننده الزامی است.");
        }

        if (initialQuantity < 0)
        {
            return Result<StockItem>.Failure(
                "stock_negative_initial",
                "موجودی اولیه نمی‌تواند منفی باشد.");
        }

        return Result<StockItem>.Success(
            new StockItem(
                Guid.NewGuid(),
                skuId,
                supplierId,
                initialQuantity,
                reservedQuantity: 0,
                now));
    }

    /// <summary>
    /// Moves stock from available to reserved.
    ///
    /// Refuses rather than clamping: a silent clamp would turn a short
    /// reservation into a smaller one, and the caller would believe the whole
    /// quantity is held when it is not (§39).
    /// </summary>
    public Result Reserve(int quantity, DateTimeOffset now)
    {
        if (quantity <= 0)
        {
            return Result.Failure("reserve_non_positive", "مقدار رزرو باید بزرگ‌تر از صفر باشد.");
        }

        if (AvailableQuantity < quantity)
        {
            return Result.Failure(
                "insufficient_stock",
                $"موجودی کافی نیست. موجودی: {AvailableQuantity}، درخواست: {quantity}.");
        }

        AvailableQuantity -= quantity;
        ReservedQuantity += quantity;
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>
    /// Returns reserved stock to available.
    ///
    /// The quantity is clamped to what is actually reserved. A release for more
    /// than was held can happen legitimately — a retried request after a
    /// partial failure — and must not push AvailableQuantity above the total,
    /// which would create stock that was never there (§6).
    /// </summary>
    public Result Release(int quantity, DateTimeOffset now)
    {
        if (quantity <= 0)
        {
            return Result.Failure("release_non_positive", "مقدار آزادسازی باید بزرگ‌تر از صفر باشد.");
        }

        var toRelease = Math.Min(quantity, ReservedQuantity);

        if (toRelease == 0)
        {
            return Result.Failure(
                "release_not_reserved",
                "هیچ موجودی رزروشده‌ای برای آزادسازی وجود ندارد.");
        }

        ReservedQuantity -= toRelease;
        AvailableQuantity += toRelease;
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>
    /// Consumes reserved stock permanently — the order was paid and shipped.
    ///
    /// Clamped for the same reason as Release: a duplicate call after a
    /// successful deduction must not deduct twice.
    /// </summary>
    public Result Deduct(int quantity, DateTimeOffset now)
    {
        if (quantity <= 0)
        {
            return Result.Failure("deduct_non_positive", "مقدار کسر باید بزرگ‌تر از صفر باشد.");
        }

        var toDeduct = Math.Min(quantity, ReservedQuantity);

        if (toDeduct == 0)
        {
            return Result.Failure(
                "deduct_not_reserved",
                "هیچ موجودی رزروشده‌ای برای کسر وجود ندارد.");
        }

        ReservedQuantity -= toDeduct;
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>
    /// Raises or lowers available stock — a correction, a damaged unit, a
    /// count that disagreed with the system.
    ///
    /// Deliberately does not touch reserved stock. A correction to physical
    /// stock cannot silently move units that an order is holding, or the
    /// reservation would no longer be satisfiable.
    /// </summary>
    public Result Adjust(int newAvailableQuantity, DateTimeOffset now)
    {
        if (newAvailableQuantity < 0)
        {
            return Result.Failure(
                "adjust_negative",
                "موجودی قابل تنظیم نمی‌تواند منفی باشد.");
        }

        AvailableQuantity = newAvailableQuantity;
        UpdatedAt = now;

        return Result.Success();
    }
}