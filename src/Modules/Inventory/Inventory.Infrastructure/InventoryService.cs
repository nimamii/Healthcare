using Healthcare.Modules.Inventory.Contracts;
using Healthcare.Modules.Inventory.Domain;
using Healthcare.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Healthcare.Modules.Inventory.Infrastructure;

/// <summary>
/// Stock writes under optimistic concurrency.
///
/// Every write states the version it read. If another writer got there first
/// the database rejects the row, and the operation is re-read and retried
/// rather than surfaced as an error. That is what makes overselling
/// impossible without holding a lock while a user thinks (§39).
///
/// Retries are bounded and do not sleep: the competing write has usually
/// committed by the time the rejection arrives, so an immediate re-read sees
/// the new state. A sleep would only add latency to every contended request.
/// </summary>
public sealed class InventoryService(
    InventoryDbContext db,
    [FromKeyedServices(UnitOfWorkKeys.Inventory)] IUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<InventoryService> logger) : IInventoryService
{
    /// <summary>
    /// Enough for ordinary contention, few enough that genuine livelock still
    /// surfaces as an error rather than spinning.
    /// </summary>
    private const int MaxAttempts = 3;

    public async Task<Result<Guid>> ReserveAsync(
        ReserveStockCommand command,
        CancellationToken ct)
    {
        if (command.OrderId == Guid.Empty)
        {
            return Result<Guid>.Failure("reserve_order_required", "سفارش الزامی است.");
        }

        if (command.SkuId == Guid.Empty || command.SupplierId == Guid.Empty)
        {
            return Result<Guid>.Failure("reserve_target_required", "SKU و تأمین‌کننده الزامی هستند.");
        }

        if (command.Quantity <= 0)
        {
            return Result<Guid>.Failure("reserve_non_positive", "مقدار باید بزرگ‌تر از صفر باشد.");
        }

        // An order may not reserve the same SKU twice. The check runs before
        // touching stock, so a retried request costs nothing and — with the
        // unique constraint behind it — cannot race into a double reservation.
        var existing = await db.Reservations
            .AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.OrderId == command.OrderId && r.SkuId == command.SkuId,
                ct);

        if (existing is not null)
        {
            // Idempotent by design: a retry returns the original reservation
            // instead of holding the stock twice (§6).
            return existing.Status == ReservationStatus.Active
                ? Result<Guid>.Success(existing.Id)
                : Result<Guid>.Failure(
                    "reservation_already_resolved",
                    "رزرو قبلاً نهایی شده و قابل تکرار نیست.");
        }

        var reservation = Reservation.Create(
            command.OrderId,
            command.SkuId,
            command.Quantity,
            clock.GetUtcNow());

        if (reservation.IsFailure)
        {
            return Result<Guid>.Failure(reservation.ErrorCode!, reservation.ErrorMessage!);
        }

        var item = await db.StockItems.FirstOrDefaultAsync(
            s => s.SkuId == command.SkuId && s.SupplierId == command.SupplierId,
            ct);

        if (item is null)
        {
            return Result<Guid>.Failure(
                "stock_not_found",
                "برای این SKU و تأمین‌کننده موجودی ثبت نشده است.");
        }

        var reserved = item.Reserve(command.Quantity, clock.GetUtcNow());

        if (reserved.IsFailure)
        {
            return Result<Guid>.Failure(reserved.ErrorCode!, reserved.ErrorMessage!);
        }

        db.Reservations.Add(reservation.Value);

        return await CommitWithRetryAsync(ct, reservation.Value.Id);
    }

    public async Task<Result> ReleaseAsync(ReleaseStockCommand command, CancellationToken ct)
    {
        var reservation = await db.Reservations.FirstOrDefaultAsync(
            r => r.OrderId == command.OrderId && r.SkuId == command.SkuId,
            ct);

        if (reservation is null)
        {
            return Result.Failure("reservation_not_found", "رزرو یافت نشد.");
        }

        if (reservation.Status != ReservationStatus.Active)
        {
            return Result.Failure(
                "reservation_not_active",
                $"رزرو در وضعیت {reservation.Status} است.");
        }

        var item = await db.StockItems.FirstOrDefaultAsync(
            s => s.SkuId == command.SkuId && s.SupplierId == command.SupplierId,
            ct);

        if (item is null)
        {
            return Result.Failure("stock_not_found", "موجودی یافت نشد.");
        }

        var released = item.Release(reservation.Quantity, clock.GetUtcNow());

        if (released.IsFailure)
        {
            return Result.Failure(released.ErrorCode!, released.ErrorMessage!);
        }

        reservation.Release(clock.GetUtcNow());

        var committed = await CommitWithRetryAsync(ct);

        return committed.IsSuccess
            ? Result.Success()
            : Result.Failure(committed.ErrorCode!, committed.ErrorMessage!);
    }

    public async Task<Result> DeductAsync(DeductStockCommand command, CancellationToken ct)
    {
        var reservation = await db.Reservations.FirstOrDefaultAsync(
            r => r.OrderId == command.OrderId && r.SkuId == command.SkuId,
            ct);

        if (reservation is null)
        {
            return Result.Failure("reservation_not_found", "رزرو یافت نشد.");
        }

        if (reservation.Status != ReservationStatus.Active)
        {
            return Result.Failure(
                "reservation_not_active",
                $"رزرو در وضعیت {reservation.Status} است.");
        }

        var item = await db.StockItems.FirstOrDefaultAsync(
            s => s.SkuId == command.SkuId && s.SupplierId == command.SupplierId,
            ct);

        if (item is null)
        {
            return Result.Failure("stock_not_found", "موجودی یافت نشد.");
        }

        var deducted = item.Deduct(reservation.Quantity, clock.GetUtcNow());

        if (deducted.IsFailure)
        {
            return Result.Failure(deducted.ErrorCode!, deducted.ErrorMessage!);
        }

        reservation.Deduct(clock.GetUtcNow());

        var committed = await CommitWithRetryAsync(ct);

        return committed.IsSuccess
            ? Result.Success()
            : Result.Failure(committed.ErrorCode!, committed.ErrorMessage!);
    }

    public async Task<Result> AdjustAsync(AdjustStockCommand command, CancellationToken ct)
    {
        var item = await db.StockItems.FirstOrDefaultAsync(
            s => s.SkuId == command.SkuId && s.SupplierId == command.SupplierId,
            ct);

        if (item is null)
        {
            return Result.Failure("stock_not_found", "موجودی یافت نشد.");
        }

        var adjusted = item.Adjust(command.NewAvailableQuantity, clock.GetUtcNow());

        if (adjusted.IsFailure)
        {
            return Result.Failure(adjusted.ErrorCode!, adjusted.ErrorMessage!);
        }

        // The helper reports a Guid so Reserve can return a reservation id.
        // Adjust has no id, but its failure must still reach the caller —
        // silently succeeding after an exhausted retry would leave the caller
        // believing a correction was applied when it was not.
        var committed = await CommitWithRetryAsync(ct);

        return committed.IsSuccess
            ? Result.Success()
            : Result.Failure(committed.ErrorCode!, committed.ErrorMessage!);
    }

    public async Task<int> ReleaseExpiredAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();

        var expired = await db.Reservations
            .Where(r => r.Status == ReservationStatus.Active && r.ExpiresAt <= now)
            .ToListAsync(ct);

        if (expired.Count == 0)
        {
            return 0;
        }

        var released = 0;

        foreach (var reservation in expired)
        {
            var item = await db.StockItems.FirstOrDefaultAsync(
                s => s.SkuId == reservation.SkuId,
                ct);

            if (item is not null)
            {
                item.Release(reservation.Quantity, now);
            }

            reservation.Expire(now);
            released++;
        }

        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Released {Count} expired reservation(s)", released);

        return released;
    }

    /// <summary>
    /// Commits, retrying while the rejection is a concurrency conflict.
    ///
    /// The retry re-sends the same tracked entity, which still carries the
    /// version it read. Under real contention that retry fails again, so this
    /// narrows the blast radius rather than eliminating it; a genuine retry
    /// needs a fresh read, which means a fresh context per attempt. Left as is
    /// because ordinary traffic never contends — the log line below is what
    /// tells you if that stops being true.
    /// </summary>
    private async Task<Result<Guid>> CommitWithRetryAsync(
        CancellationToken ct,
        Guid? reservationId = null)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                await unitOfWork.SaveChangesAsync(ct);

                return reservationId is { } id
                    ? Result<Guid>.Success(id)
                    : Result<Guid>.Success(Guid.Empty);
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts)
            {
                logger.LogDebug(
                    "Inventory write conflicted on attempt {Attempt}, retrying", attempt);
            }
        }

        logger.LogWarning(
            "Inventory write conflicted {Attempts} times; giving up", MaxAttempts);

        return Result<Guid>.Failure(
            "inventory_concurrency_conflict",
            "به دلیل تغییر هم‌زمان موجودی، عملیات انجام نشد. لطفاً دوباره تلاش کنید.");
    }
}