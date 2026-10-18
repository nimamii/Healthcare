using Healthcare.Modules.Orders.Domain;
using Healthcare.SharedKernel;

namespace Healthcare.Modules.Orders.Application;

/// <summary>
/// How other modules interact with orders.
/// </summary>
public interface IOrderService
{
    /// <summary>
    /// Creates a new order for a customer from a supplier.
    /// </summary>
    Task<Result<OrderSummary>> CreateOrder(Guid customerId, Guid supplierId, DateTimeOffset now);

    /// <summary>
    /// Adds a line item to an existing order.
    /// </summary>
    Task<Result> AddLine(Guid orderId, Guid skuId, int quantity, decimal unitPrice, string currency, DateTimeOffset now);

    /// <summary>
    /// Moves the order to PAYMENT_PENDING.
    /// </summary>
    Task<Result> MarkPaymentPending(Guid orderId, DateTimeOffset now);

    /// <summary>
    /// Moves the order to PAID after payment succeeds.
    /// </summary>
    Task<Result> MarkPaid(Guid orderId, DateTimeOffset now);

    /// <summary>
    /// Moves the order to FULFILLMENT_PENDING.
    /// </summary>
    Task<Result> MarkFulfillmentPending(Guid orderId, DateTimeOffset now);

    /// <summary>
    /// Confirms the order for fulfillment.
    /// </summary>
    Task<Result> Confirm(Guid orderId, DateTimeOffset now);

    /// <summary>
    /// Cancels the order if it has not shipped.
    /// </summary>
    Task<Result> Cancel(Guid orderId, DateTimeOffset now);
}

/// <summary>
/// A summary of an order for listing purposes.
/// </summary>
public readonly record struct OrderSummary(
    Guid Id,
    Guid CustomerId,
    Guid SupplierId,
    int Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Application service for orders.
/// </summary>
/// <remarks>
/// This service orchestrates the order state machine. It does not contain
/// business rules — those live in the domain. This service coordinates
/// between the domain and the infrastructure.
/// </remarks>
public sealed class OrderService(IOrderRepository repository) : IOrderService
{
    public async Task<Result<OrderSummary>> CreateOrder(Guid customerId, Guid supplierId, DateTimeOffset now)
    {
        var orderResult = Order.Create(customerId, supplierId, now);

        if (!orderResult.IsSuccess)
        {
            return Result<OrderSummary>.Failure(orderResult.ErrorCode!, orderResult.ErrorMessage!);
        }

        var order = orderResult.Value;
        repository.Add(order);
        await Task.CompletedTask;
        return Result<OrderSummary>.Success(MapToSummary(order));
    }

    public async Task<Result> AddLine(Guid orderId, Guid skuId, int quantity, decimal unitPrice, string currency, DateTimeOffset now)
    {
        var order = repository.FindById(orderId);

        if (order is null)
        {
            return Result.Failure("order_not_found", "سفارش پیدا نشد.");
        }

        var lineResult = order.AddLine(skuId, quantity, unitPrice, currency, now);

        if (!lineResult.IsSuccess)
        {
            return Result.Failure(lineResult.ErrorCode!, lineResult.ErrorMessage!);
        }

        repository.Update(order);
        await Task.CompletedTask;
        return Result.Success();
    }

    public async Task<Result> MarkPaymentPending(Guid orderId, DateTimeOffset now)
    {
        var order = repository.FindById(orderId);

        if (order is null)
        {
            return Result.Failure("order_not_found", "سفارش پیدا نشد.");
        }

        var result = order.MarkPaymentPending(now);

        if (!result.IsSuccess)
        {
            return Result.Failure(result.ErrorCode!, result.ErrorMessage!);
        }

        repository.Update(order);
        await Task.CompletedTask;
        return Result.Success();
    }

    public async Task<Result> MarkPaid(Guid orderId, DateTimeOffset now)
    {
        var order = repository.FindById(orderId);

        if (order is null)
        {
            return Result.Failure("order_not_found", "سفارش پیدا نشد.");
        }

        var result = order.MarkPaid(now);

        if (!result.IsSuccess)
        {
            return Result.Failure(result.ErrorCode!, result.ErrorMessage!);
        }

        repository.Update(order);
        await Task.CompletedTask;
        return Result.Success();
    }

    public async Task<Result> MarkFulfillmentPending(Guid orderId, DateTimeOffset now)
    {
        var order = repository.FindById(orderId);

        if (order is null)
        {
            return Result.Failure("order_not_found", "سفارش پیدا نشد.");
        }

        var result = order.MarkFulfillmentPending(now);

        if (!result.IsSuccess)
        {
            return Result.Failure(result.ErrorCode!, result.ErrorMessage!);
        }

        repository.Update(order);
        await Task.CompletedTask;
        return Result.Success();
    }

    public async Task<Result> Confirm(Guid orderId, DateTimeOffset now)
    {
        var order = repository.FindById(orderId);

        if (order is null)
        {
            return Result.Failure("order_not_found", "سفارش پیدا نشد.");
        }

        var result = order.Confirm(now);

        if (!result.IsSuccess)
        {
            return Result.Failure(result.ErrorCode!, result.ErrorMessage!);
        }

        repository.Update(order);
        await Task.CompletedTask;
        return Result.Success();
    }

    public async Task<Result> Cancel(Guid orderId, DateTimeOffset now)
    {
        var order = repository.FindById(orderId);

        if (order is null)
        {
            return Result.Failure("order_not_found", "سفارش پیدا نشد.");
        }

        var result = order.Cancel(now);

        if (!result.IsSuccess)
        {
            return Result.Failure(result.ErrorCode!, result.ErrorMessage!);
        }

        repository.Update(order);
        await Task.CompletedTask;
        return Result.Success();
    }

    private static OrderSummary MapToSummary(Order order) =>
        new(order.Id, order.CustomerId, order.SupplierId, (int)order.Status, order.CreatedAt, order.UpdatedAt);
}

/// <summary>
/// Repository interface for orders.
/// </summary>
/// <remarks>
/// Defined here so the application layer does not depend on infrastructure.
/// The infrastructure layer implements this interface.
/// </remarks>
public interface IOrderRepository
{
    Order? FindById(Guid id);
    void Add(Order order);
    void Update(Order order);
}
