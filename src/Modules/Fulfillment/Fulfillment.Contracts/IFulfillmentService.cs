using Healthcare.Modules.Geography.Contracts;
using Healthcare.SharedKernel;

namespace Healthcare.Modules.Fulfillment.Contracts;

public interface IFulfillmentService
{
    Task<Result<IReadOnlyCollection<SupplierCandidate>>> RankSuppliersAsync(Guid skuId, GeoPoint deliveryLocation, CancellationToken ct);
    Task<Result<Guid>> ReserveAsync(Guid orderId, Guid skuId, Guid supplierId, int quantity, CancellationToken ct);
}

public readonly record struct SupplierCandidate(Guid SupplierId, int AvailableQuantity, decimal? ListPrice, bool InArea, bool RequiresConfirmation);
