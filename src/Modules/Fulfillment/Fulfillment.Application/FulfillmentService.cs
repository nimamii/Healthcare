using Healthcare.Modules.Catalog.Contracts;
using Healthcare.Modules.Fulfillment.Contracts;
using Healthcare.Modules.Fulfillment.Domain;
using Healthcare.Modules.Geography.Contracts;
using Healthcare.Modules.Inventory.Contracts;
using Healthcare.SharedKernel;

namespace Healthcare.Modules.Fulfillment.Application;

public sealed class FulfillmentService(
    IProductCatalog catalog,
    IInventoryReader inventory,
    IGeographyReader geography,
    IInventoryService inventoryWriter) : IFulfillmentService
{
    public async Task<Result<IReadOnlyCollection<SupplierCandidate>>> RankSuppliersAsync(Guid skuId, GeoPoint deliveryLocation, CancellationToken ct)
    {
        var stock = await inventory.GetForSkuAsync(skuId, ct);
        if (stock.Count == 0) return Result<IReadOnlyCollection<SupplierCandidate>>.Failure("fulfillment_no_stock", "No supplier has this SKU in stock.");

        var candidates = new List<SupplierCandidate>();
        foreach (var level in stock)
        {
            var eligibility = await geography.EvaluateAsync(level.SupplierId, deliveryLocation, ct);
            var listing = (await catalog.GetListingsAsync(skuId, ct)).FirstOrDefault(l => l.SupplierId == level.SupplierId);
            candidates.Add(new SupplierCandidate(level.SupplierId, level.AvailableQuantity, listing.ListPrice, eligibility.IsInArea, eligibility.RequiresCustomerConfirmation));
        }

        return Result<IReadOnlyCollection<SupplierCandidate>>.Success(candidates.OrderByDescending(c => c.AvailableQuantity).ToList());
    }

    public async Task<Result<Guid>> ReserveAsync(Guid orderId, Guid skuId, Guid supplierId, int quantity, CancellationToken ct)
    {
        var stock = await inventory.GetAsync(skuId, supplierId, ct);
        if (stock is null || stock.Value.AvailableQuantity < quantity)
        {
            return Result<Guid>.Failure("fulfillment_insufficient_stock", "The selected supplier does not have enough available stock.");
        }

        var reservation = await inventoryWriter.ReserveAsync(new ReserveStockCommand(orderId, skuId, supplierId, quantity), ct);
        if (!reservation.IsSuccess) return Result<Guid>.Failure(reservation.ErrorCode!, reservation.ErrorMessage!);
        return reservation;
    }
}
