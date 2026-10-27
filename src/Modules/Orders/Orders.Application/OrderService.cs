using Healthcare.Modules.Orders.Domain;
using Healthcare.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Healthcare.Modules.Orders.Application;

public interface IOrderService
{
    Task<Result<OrderSummary>> CreateOrder(Guid customerId, Guid supplierId, DateTimeOffset now, CancellationToken ct = default);

    Task<Result> AddLine(
        Guid orderId,
        Guid skuId,
        int quantity,
        decimal unitPrice,
        string currency,
        DateTimeOffset now,
        string? lotNumber = null,
        string? serialNumber = null,
        DateTimeOffset? expiryDate = null,
        CancellationToken ct = default);

    Task<Result> SetB2BTerms(Guid orderId, string poNumber, string terms, DateTimeOffset now, CancellationToken ct = default);

    Task<Result> VerifyCompliance(Guid orderId, string licenseNumber, DateTimeOffset now, CancellationToken ct = default);

    Task<Result> MarkPaymentPending(Guid orderId, DateTimeOffset now, CancellationToken ct = default);

    Task<Result> MarkPaid(Guid orderId, DateTimeOffset now, CancellationToken ct = default);

    Task<Result> MarkFulfillmentPending(Guid orderId, DateTimeOffset now, CancellationToken ct = default);

    Task<Result> Confirm(Guid orderId, DateTimeOffset now, CancellationToken ct = default);

    Task<Result> Cancel(Guid orderId, DateTimeOffset now, CancellationToken ct = default);
}

public readonly record struct OrderSummary(
    Guid Id,
    Guid CustomerId,
    Guid SupplierId,
    int Status,
    int PaymentMethod,
    string? PurchaseOrderNumber,
    string? PaymentTerms,
    string? MedicalLicenseNumber,
    bool IsPrescriptionVerified,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed class OrderService(
    IOrderRepository repository,
    [FromKeyedServices(UnitOfWorkKeys.Orders)] IUnitOfWork? unitOfWork = null) : IOrderService
{
    public async Task<Result<OrderSummary>> CreateOrder(Guid customerId, Guid supplierId, DateTimeOffset now, CancellationToken ct = default)
    {
        var orderResult = Order.Create(customerId, supplierId, now);

        if (!orderResult.IsSuccess)
        {
            return Result<OrderSummary>.Failure(orderResult.ErrorCode!, orderResult.ErrorMessage!);
        }

        var order = orderResult.Value;
        repository.Add(order);
        if (unitOfWork is not null) await unitOfWork.SaveChangesAsync(ct);
        return Result<OrderSummary>.Success(MapToSummary(order));
    }

    public async Task<Result> AddLine(
        Guid orderId,
        Guid skuId,
        int quantity,
        decimal unitPrice,
        string currency,
        DateTimeOffset now,
        string? lotNumber = null,
        string? serialNumber = null,
        DateTimeOffset? expiryDate = null,
        CancellationToken ct = default)
    {
        var order = repository.FindById(orderId);
        if (order is null) return Result.Failure("order_not_found", "سفارش پیدا نشد.");

        var lineResult = order.AddLine(skuId, quantity, unitPrice, currency, now, lotNumber, serialNumber, expiryDate);
        if (!lineResult.IsSuccess) return Result.Failure(lineResult.ErrorCode!, lineResult.ErrorMessage!);

        repository.Update(order);
        if (unitOfWork is not null) await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> SetB2BTerms(Guid orderId, string poNumber, string terms, DateTimeOffset now, CancellationToken ct = default)
    {
        var order = repository.FindById(orderId);
        if (order is null) return Result.Failure("order_not_found", "سفارش پیدا نشد.");

        var result = order.SetB2BPurchaseOrder(poNumber, terms, now);
        if (!result.IsSuccess) return Result.Failure(result.ErrorCode!, result.ErrorMessage!);

        repository.Update(order);
        if (unitOfWork is not null) await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> VerifyCompliance(Guid orderId, string licenseNumber, DateTimeOffset now, CancellationToken ct = default)
    {
        var order = repository.FindById(orderId);
        if (order is null) return Result.Failure("order_not_found", "سفارش پیدا نشد.");

        var result = order.VerifyMedicalCompliance(licenseNumber, now);
        if (!result.IsSuccess) return Result.Failure(result.ErrorCode!, result.ErrorMessage!);

        repository.Update(order);
        if (unitOfWork is not null) await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> MarkPaymentPending(Guid orderId, DateTimeOffset now, CancellationToken ct = default)
    {
        var order = repository.FindById(orderId);
        if (order is null) return Result.Failure("order_not_found", "سفارش پیدا نشد.");

        var result = order.MarkPaymentPending(now);
        if (!result.IsSuccess) return Result.Failure(result.ErrorCode!, result.ErrorMessage!);

        repository.Update(order);
        if (unitOfWork is not null) await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> MarkPaid(Guid orderId, DateTimeOffset now, CancellationToken ct = default)
    {
        var order = repository.FindById(orderId);
        if (order is null) return Result.Failure("order_not_found", "سفارش پیدا نشد.");

        var result = order.MarkPaid(now);
        if (!result.IsSuccess) return Result.Failure(result.ErrorCode!, result.ErrorMessage!);

        repository.Update(order);
        if (unitOfWork is not null) await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> MarkFulfillmentPending(Guid orderId, DateTimeOffset now, CancellationToken ct = default)
    {
        var order = repository.FindById(orderId);
        if (order is null) return Result.Failure("order_not_found", "سفارش پیدا نشد.");

        var result = order.MarkFulfillmentPending(now);
        if (!result.IsSuccess) return Result.Failure(result.ErrorCode!, result.ErrorMessage!);

        repository.Update(order);
        if (unitOfWork is not null) await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> Confirm(Guid orderId, DateTimeOffset now, CancellationToken ct = default)
    {
        var order = repository.FindById(orderId);
        if (order is null) return Result.Failure("order_not_found", "سفارش پیدا نشد.");

        var result = order.Confirm(now);
        if (!result.IsSuccess) return Result.Failure(result.ErrorCode!, result.ErrorMessage!);

        repository.Update(order);
        if (unitOfWork is not null) await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> Cancel(Guid orderId, DateTimeOffset now, CancellationToken ct = default)
    {
        var order = repository.FindById(orderId);
        if (order is null) return Result.Failure("order_not_found", "سفارش پیدا نشد.");

        var result = order.Cancel(now);
        if (!result.IsSuccess) return Result.Failure(result.ErrorCode!, result.ErrorMessage!);

        repository.Update(order);
        if (unitOfWork is not null) await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    private static OrderSummary MapToSummary(Order order) =>
        new(
            order.Id,
            order.CustomerId,
            order.SupplierId,
            (int)order.Status,
            (int)order.PaymentMethod,
            order.PurchaseOrderNumber,
            order.PaymentTerms,
            order.MedicalLicenseNumber,
            order.IsPrescriptionVerified,
            order.CreatedAt,
            order.UpdatedAt);
}

public interface IOrderRepository
{
    Order? FindById(Guid id);
    void Add(Order order);
    void Update(Order order);
}
