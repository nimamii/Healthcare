using Healthcare.SharedKernel;

namespace Healthcare.Modules.Inventory.Contracts;

/// <summary>
/// Writes to stock. The read side is on Contracts, because other modules look
/// at stock constantly but change it almost never.
/// </summary>
public interface IInventoryService
{
    /// <summary>
    /// Holds stock for an order.
    ///
    /// Retries internally when a concurrent write wins, so a caller sees one
    /// outcome rather than a concurrency exception. Returns the reservation id.
    /// </summary>
    Task<Result<Guid>> ReserveAsync(ReserveStockCommand command, CancellationToken ct);

    /// <summary>Returns reserved stock after a cancellation.</summary>
    Task<Result> ReleaseAsync(ReleaseStockCommand command, CancellationToken ct);

    /// <summary>Consumes reserved stock once the order is paid and shipped.</summary>
    Task<Result> DeductAsync(DeductStockCommand command, CancellationToken ct);

    /// <summary>Corrects a supplier's count.</summary>
    Task<Result> AdjustAsync(AdjustStockCommand command, CancellationToken ct);

    /// <summary>
    /// Releases every reservation that has aged out. Run periodically; the
    /// spec requires the system to recover from abandoned checkouts (§6).
    /// </summary>
    Task<int> ReleaseExpiredAsync(CancellationToken ct);
}

public sealed record ReserveStockCommand(
    Guid OrderId,
    Guid SkuId,
    Guid SupplierId,
    int Quantity);

public sealed record ReleaseStockCommand(
    Guid OrderId,
    Guid SkuId,
    Guid SupplierId);

public sealed record DeductStockCommand(
    Guid OrderId,
    Guid SkuId,
    Guid SupplierId);

public sealed record AdjustStockCommand(
    Guid SkuId,
    Guid SupplierId,
    int NewAvailableQuantity);
