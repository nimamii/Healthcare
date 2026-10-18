using Healthcare.SharedKernel;

namespace Healthcare.Modules.Inventory.Domain;

/// <summary>
/// Stock held for an order that has not been paid or cancelled.
///
/// Reservations expire. Without that, a customer who closes the browser after
/// reserving ten units keeps them out of circulation forever, and the spec
/// requires the system to survive failed payments (§6).
///
/// The expiry is decided by the server. A browser-computed expiry would let a
/// client hold stock by lying about its clock, and would drift on a long-lived
/// checkout.
/// </summary>
public sealed class Reservation
{
    private Reservation()
    {
        // EF Core.
    }

    private Reservation(
        Guid id,
        Guid orderId,
        Guid skuId,
        int quantity,
        DateTimeOffset expiresAt,
        DateTimeOffset now)
    {
        Id = id;
        OrderId = orderId;
        SkuId = skuId;
        Quantity = quantity;
        Status = ReservationStatus.Active;
        ExpiresAt = expiresAt;
        CreatedAt = now;
    }

    /// <summary>
    /// How long a reservation holds stock before the sweep releases it.
    ///
    /// Long enough to cover checkout and a payment provider's slowest response,
    /// short enough that an abandoned cart does not strangle the catalogue.
    /// </summary>
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(15);

    public Guid Id { get; private set; }

    /// <summary>No foreign key: Orders arrives in a later phase.</summary>
    public Guid OrderId { get; private set; }

    /// <summary>No foreign key: Catalog owns this reference.</summary>
    public Guid SkuId { get; private set; }

    public int Quantity { get; private set; }

    public ReservationStatus Status { get; private set; }

    /// <summary>Server time at which this stops holding stock.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public static Result<Reservation> Create(
        Guid orderId,
        Guid skuId,
        int quantity,
        DateTimeOffset now,
        TimeSpan? lifetime = null)
    {
        if (orderId == Guid.Empty)
        {
            return Result<Reservation>.Failure("reservation_order_required", "سفارش الزامی است.");
        }

        if (skuId == Guid.Empty)
        {
            return Result<Reservation>.Failure("reservation_sku_required", "SKU الزامی است.");
        }

        if (quantity <= 0)
        {
            return Result<Reservation>.Failure(
                "reservation_non_positive",
                "مقدار رزرو باید بزرگ‌تر از صفر باشد.");
        }

        var span = lifetime ?? DefaultLifetime;

        if (span <= TimeSpan.Zero)
        {
            return Result<Reservation>.Failure(
                "reservation_lifetime_invalid",
                "مدت اعتبار رزرو باید بزرگ‌تر از صفر باشد.");
        }

        return Result<Reservation>.Success(
            new Reservation(
                Guid.NewGuid(),
                orderId,
                skuId,
                quantity,
                now + span,
                now));
    }

    /// <summary>
    /// Whether the sweep should treat this as expired.
    ///
    /// Evaluated against server time. The caller passes it in rather than the
    /// entity reading a clock, so the same rule can be tested without a clock.
    /// </summary>
    public bool IsExpiredAt(DateTimeOffset now) =>
        Status == ReservationStatus.Active && ExpiresAt <= now;

    /// <summary>
    /// Ends the reservation without returning stock — the order was paid and
    /// the units left the building.
    /// </summary>
    public Result Deduct(DateTimeOffset now)
    {
        if (Status != ReservationStatus.Active)
        {
            return Result.Failure(
                "reservation_not_active",
                $"رزرو در وضعیت {Status} است و نمی‌توان آن را کسر کرد.");
        }

        Status = ReservationStatus.Deducted;
        ResolvedAt = now;

        return Result.Success();
    }

    /// <summary>
    /// Ends the reservation and returns the stock.
    ///
    /// Idempotent by outcome, not by return value: releasing an already
    /// released reservation fails, but the caller has already been told, and
    /// the sweep that runs periodically will simply skip it next time.
    /// </summary>
    public Result Release(DateTimeOffset now)
    {
        if (Status != ReservationStatus.Active)
        {
            return Result.Failure(
                "reservation_not_active",
                $"رزرو در وضعیت {Status} است و نمی‌توان آن را آزاد کرد.");
        }

        Status = ReservationStatus.Released;
        ResolvedAt = now;

        return Result.Success();
    }

    /// <summary>
    /// Releases because it aged out rather than because an order changed.
    /// Kept distinct from Release so the audit trail separates "the customer
    /// cancelled" from "they walked away", which are different events (§23).
    /// </summary>
    public Result Expire(DateTimeOffset now)
    {
        if (Status != ReservationStatus.Active)
        {
            return Result.Failure(
                "reservation_not_active",
                $"رزرو در وضعیت {Status} است و نمی‌توان منقضی شود.");
        }

        Status = ReservationStatus.Expired;
        ResolvedAt = now;

        return Result.Success();
    }
}

public enum ReservationStatus
{
    /// <summary>Holding stock, counting down to ExpiresAt.</summary>
    Active = 0,

    /// <summary>Stock returned — cancelled, or aged out.</summary>
    Released = 1,

    /// <summary>Stock consumed — the order was paid and shipped.</summary>
    Deducted = 2,

    /// <summary>Expired without being claimed. Distinguishable from Released.</summary>
    Expired = 3,
}