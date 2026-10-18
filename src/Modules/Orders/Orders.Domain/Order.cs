using Healthcare.SharedKernel;

namespace Healthcare.Modules.Orders.Domain;

/// <summary>
/// An order in the marketplace.
/// </summary>
/// <remarks>
/// The order state machine is: CREATED → PAYMENT_PENDING → PAID → FULFILLMENT_PENDING → CONFIRMED → PROCESSING → SHIPPED → DELIVERED
/// (+ CANCELLED/FAILED/REFUNDED)
///
/// Every transition is explicit and guarded. The state is not a free-form
/// string; it is an enum that the compiler can check.
/// </remarks>
public sealed class Order
{
    private Order()
    {
        // EF Core.
    }

    private Order(
        Guid id,
        Guid customerId,
        Guid supplierId,
        OrderStatus status,
        DateTimeOffset now)
    {
        Id = id;
        CustomerId = customerId;
        SupplierId = supplierId;
        Status = status;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid CustomerId { get; private set; }

    public Guid SupplierId { get; private set; }

    public OrderStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<OrderLine> Lines => _lines.AsReadOnly();
    private List<OrderLine> _lines = new();

    public static Result<Order> Create(
        Guid customerId,
        Guid supplierId,
        DateTimeOffset now)
    {
        if (customerId == Guid.Empty)
        {
            return Result<Order>.Failure(
                "order_customer_required",
                "مشتری الزامی است.");
        }

        if (supplierId == Guid.Empty)
        {
            return Result<Order>.Failure(
                "order_supplier_required",
                "تأمین‌کننده الزامی است.");
        }

        return Result<Order>.Success(new Order(
            Guid.NewGuid(),
            customerId,
            supplierId,
            OrderStatus.Created,
            now));
    }

    public Result AddLine(Guid skuId, int quantity, decimal unitPrice, string currency, DateTimeOffset now)
    {
        if (skuId == Guid.Empty)
        {
            return Result.Failure(
                "order_line_sku_required",
                "SKU الزامی است.");
        }

        if (quantity <= 0)
        {
            return Result.Failure(
                "order_line_quantity_invalid",
                "تعداد باید بزرگ‌تر از صفر باشد.");
        }

        if (unitPrice < 0)
        {
            return Result.Failure(
                "order_line_price_invalid",
                "قیمت نمی‌تواند منفی باشد.");
        }

        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
        {
            return Result.Failure(
                "order_line_currency_invalid",
                "کد ارز باید سه حرف باشد.");
        }

        _lines.Add(new OrderLine(Guid.NewGuid(), skuId, quantity, unitPrice, currency));
        UpdatedAt = now;
        return Result.Success();
    }

    public Result MarkPaymentPending(DateTimeOffset now)
    {
        if (Status != OrderStatus.Created)
        {
            return Result.Failure(
                "order_invalid_transition",
                $"سفارش در وضعیت {Status} نمی‌تواند به PAYMENT_PENDING برود.");
        }

        Status = OrderStatus.PaymentPending;
        UpdatedAt = now;
        return Result.Success();
    }

    public Result MarkPaid(DateTimeOffset now)
    {
        if (Status != OrderStatus.PaymentPending)
        {
            return Result.Failure(
                "order_invalid_transition",
                $"سفارش در وضعیت {Status} نمی‌تواند به PAID برود.");
        }

        Status = OrderStatus.Paid;
        UpdatedAt = now;
        return Result.Success();
    }

    public Result MarkFulfillmentPending(DateTimeOffset now)
    {
        if (Status != OrderStatus.Paid)
        {
            return Result.Failure(
                "order_invalid_transition",
                $"سفارش در وضعیت {Status} نمی‌تواند به FULFILLMENT_PENDING برود.");
        }

        Status = OrderStatus.FulfillmentPending;
        UpdatedAt = now;
        return Result.Success();
    }

    public Result Confirm(DateTimeOffset now)
    {
        if (Status != OrderStatus.FulfillmentPending)
        {
            return Result.Failure(
                "order_invalid_transition",
                $"سفارش در وضعیت {Status} نمی‌تواند به CONFIRMED برود.");
        }

        Status = OrderStatus.Confirmed;
        UpdatedAt = now;
        return Result.Success();
    }

    public Result Cancel(DateTimeOffset now)
    {
        if (Status is OrderStatus.Shipped or OrderStatus.Delivered)
        {
            return Result.Failure(
                "order_cannot_cancel",
                "سفارش ارسال‌شده یا تحویل‌شده را نمی‌توان لغو کرد.");
        }

        Status = OrderStatus.Cancelled;
        UpdatedAt = now;
        return Result.Success();
    }
}

public enum OrderStatus
{
    Created = 0,
    PaymentPending = 1,
    Paid = 2,
    FulfillmentPending = 3,
    Confirmed = 4,
    Processing = 5,
    Shipped = 6,
    Delivered = 7,
    Cancelled = 8,
    Failed = 9,
    Refunded = 10,
}

public sealed record OrderLine(
    Guid Id,
    Guid SkuId,
    int Quantity,
    decimal UnitPrice,
    string Currency);
