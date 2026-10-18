using Healthcare.SharedKernel;

namespace Healthcare.Modules.Fulfillment.Domain;

public sealed class FulfillmentPlan
{
    private FulfillmentPlan() { }

    private FulfillmentPlan(Guid id, Guid orderId, Guid supplierId, Guid skuId, int quantity, DateTimeOffset now)
    {
        Id = id;
        OrderId = orderId;
        SupplierId = supplierId;
        SkuId = skuId;
        Quantity = quantity;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid SupplierId { get; private set; }
    public Guid SkuId { get; private set; }
    public int Quantity { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static Result<FulfillmentPlan> Create(Guid orderId, Guid supplierId, Guid skuId, int quantity, DateTimeOffset now)
    {
        if (orderId == Guid.Empty) return Result<FulfillmentPlan>.Failure("fulfillment_order_required", "Order is required.");
        if (supplierId == Guid.Empty) return Result<FulfillmentPlan>.Failure("fulfillment_supplier_required", "Supplier is required.");
        if (skuId == Guid.Empty) return Result<FulfillmentPlan>.Failure("fulfillment_sku_required", "SKU is required.");
        if (quantity <= 0) return Result<FulfillmentPlan>.Failure("fulfillment_quantity_invalid", "Quantity must be greater than zero.");
        return Result<FulfillmentPlan>.Success(new FulfillmentPlan(Guid.NewGuid(), orderId, supplierId, skuId, quantity, now));
    }
}
