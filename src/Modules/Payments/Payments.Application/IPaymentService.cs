using Healthcare.SharedKernel;

namespace Healthcare.Modules.Payments.Application;

/// <summary>
/// Manages payments for orders. Highest risk area per spec: idempotency
/// is mandatory, and a payment is NEVER marked paid based on the client's
/// response. The server is the only source of truth.
/// </summary>
public interface IPaymentService
{
    Task<Result<PaymentSummary>> CreatePaymentAsync(
        Guid orderId, decimal amount, string currency, DateTimeOffset now);

    Task<Result> RefreshStatusAsync(Guid paymentId, DateTimeOffset now);

    Task<Result> VoidAsync(Guid paymentId, DateTimeOffset now);
}

public readonly record struct PaymentSummary(
    Guid Id, Guid OrderId, decimal Amount, string Currency, int Status, DateTimeOffset CreatedAt);
