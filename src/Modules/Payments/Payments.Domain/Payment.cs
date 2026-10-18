using Healthcare.SharedKernel;

namespace Healthcare.Modules.Payments.Domain;

/// <summary>
/// A payment against an order.
/// </summary>
/// <remarks>
/// §39 marks Payment as the highest-risk area: idempotency is mandatory,
/// and a payment must NEVER be marked paid based on the client's response.
/// The server is the only source of truth.
///
/// State machine: PENDING → AUTHORIZED → CAPTURED → SETTLED
///                    ↓           ↓          ↓
///                 FAILED      FAILED     REFUNDED
/// </remarks>
public sealed class Payment
{
    private Payment()
    {
        // EF Core.
        Currency = string.Empty;
    }

    private Payment(
        Guid id,
        Guid orderId,
        decimal amount,
        string currency,
        PaymentStatus status,
        DateTimeOffset now)
    {
        Id = id;
        OrderId = orderId;
        Amount = amount;
        Currency = currency;
        Status = status;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid OrderId { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    public PaymentStatus Status { get; private set; }

    /// <summary>
    /// The payment provider's own transaction id. Null until the provider
    /// has been called.
    /// </summary>
    public string? ProviderTransactionId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Result<Payment> Create(
        Guid orderId,
        decimal amount,
        string currency,
        DateTimeOffset now)
    {
        if (orderId == Guid.Empty)
        {
            return Result<Payment>.Failure(
                "payment_order_required",
                "سفارش الزامی است.");
        }

        if (amount <= 0)
        {
            return Result<Payment>.Failure(
                "payment_amount_invalid",
                "مبلغ باید بزرگ‌تر از صفر باشد.");
        }

        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
        {
            return Result<Payment>.Failure(
                "payment_currency_invalid",
                "کد ارز باید سه حرف باشد.");
        }

        return Result<Payment>.Success(new Payment(
            Guid.NewGuid(),
            orderId,
            amount,
            currency.ToUpperInvariant(),
            PaymentStatus.Pending,
            now));
    }

    /// <summary>
    /// Marks the payment as authorized by the provider.
    /// </summary>
    /// <remarks>
    /// This is called by the infrastructure layer after the provider
    /// confirms authorization. It is NOT called from the API directly —
    /// the API calls the provider, and the provider's callback (or poll)
    /// calls this method.
    /// </remarks>
    public Result MarkAuthorized(string providerTransactionId, DateTimeOffset now)
    {
        if (Status != PaymentStatus.Pending)
        {
            return Result.Failure(
                "payment_invalid_transition",
                $"پرداخت در وضعیت {Status} نمی‌تواند AUTHORIZED شود.");
        }

        if (string.IsNullOrWhiteSpace(providerTransactionId))
        {
            return Result.Failure(
                "payment_provider_tx_required",
                "شناسه تراکنش ارائه‌دهنده الزامی است.");
        }

        Status = PaymentStatus.Authorized;
        ProviderTransactionId = providerTransactionId;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>
    /// Marks the payment as captured (funds transferred).
    /// </summary>
    public Result MarkCaptured(DateTimeOffset now)
    {
        if (Status != PaymentStatus.Authorized)
        {
            return Result.Failure(
                "payment_invalid_transition",
                $"پرداخت در وضعیت {Status} نمی‌تواند CAPTURED شود.");
        }

        Status = PaymentStatus.Captured;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>
    /// Marks the payment as settled (funds available for payout).
    /// </summary>
    public Result MarkSettled(DateTimeOffset now)
    {
        if (Status != PaymentStatus.Captured)
        {
            return Result.Failure(
                "payment_invalid_transition",
                $"پرداخت در وضعیت {Status} نمی‌تواند SETTLED شود.");
        }

        Status = PaymentStatus.Settled;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>
    /// Marks the payment as failed.
    /// </summary>
    public Result MarkFailed(DateTimeOffset now)
    {
        if (Status is PaymentStatus.Settled or PaymentStatus.Refunded)
        {
            return Result.Failure(
                "payment_invalid_transition",
                $"پرداخت در وضعیت {Status} نمی‌تواند FAILED شود.");
        }

        Status = PaymentStatus.Failed;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>
    /// Marks the payment as refunded.
    /// </summary>
    public Result MarkRefunded(DateTimeOffset now)
    {
        if (Status != PaymentStatus.Settled)
        {
            return Result.Failure(
                "payment_invalid_transition",
                $"پرداخت در وضعیت {Status} نمی‌تواند REFUNDED شود.");
        }

        Status = PaymentStatus.Refunded;
        UpdatedAt = now;
        return Result.Success();
    }
}

public enum PaymentStatus
{
    Pending = 0,
    Authorized = 1,
    Captured = 2,
    Settled = 3,
    Failed = 4,
    Refunded = 5,
}
