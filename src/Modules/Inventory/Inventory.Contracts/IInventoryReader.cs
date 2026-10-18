namespace Healthcare.Modules.Inventory.Contracts;

/// <summary>
/// Live stock for one SKU at one supplier.
/// </summary>
/// <param name="Version">
/// The concurrency token. A caller that plans to write must read it first and
/// present it back, so a concurrent change is detected rather than silently
/// overwritten.
/// </param>
public readonly record struct StockLevel(
    Guid SkuId,
    Guid SupplierId,
    int AvailableQuantity,
    int ReservedQuantity,
    long Version)
{
    /// <summary>What a customer can still buy, which excludes reserved units.</summary>
    public bool InStock => AvailableQuantity > 0;
}

public interface IInventoryReader
{
    /// <summary>Returns null when no stock row exists for the pair.</summary>
    Task<StockLevel?> GetAsync(Guid skuId, Guid supplierId, CancellationToken ct);

    /// <summary>
    /// Every supplier stocking this SKU, for the Fulfillment Engine to rank
    /// (§8 — not simply the nearest supplier).
    /// </summary>
    Task<IReadOnlyCollection<StockLevel>> GetForSkuAsync(Guid skuId, CancellationToken ct);
}