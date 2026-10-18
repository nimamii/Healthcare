using Healthcare.Modules.Payments.Domain;
using Healthcare.SharedKernel;

namespace Healthcare.Modules.Payments.Application;

/// <summary>
/// سرویس اپلیکیشن برای پرداخت‌ها.
/// </summary>
/// <remarks>
/// این سرویس بین دامنه و زیرساخت هماهنگی ایجاد می‌کند.
/// قواعد کسب‌وکار در دامنه زندگی می‌کنند.
/// </remarks>
public sealed class PaymentService(
    IPaymentRepository repository,
    IPaymentGateway gateway) : IPaymentService
{
    public async Task<Result<PaymentSummary>> CreatePaymentAsync(
        Guid orderId,
        decimal amount,
        string currency,
        DateTimeOffset now)
    {
        var paymentResult = Payment.Create(orderId, amount, currency, now);

        if (!paymentResult.IsSuccess)
        {
            return Result<PaymentSummary>.Failure(
                paymentResult.ErrorCode!,
                paymentResult.ErrorMessage!);
        }

        var payment = paymentResult.Value;
        repository.Add(payment);

        // فراخوانی درگاه پرداخت. اگر موفق باشد، وضعیت Authorized می‌شود.
        // این تنها جایی است که ارائه‌دهنده واقعی صدا زده می‌شود.
        var authResult = gateway.Authorize(payment.Id, payment.Amount, payment.Currency);

        if (authResult.IsSuccess)
        {
            payment.MarkAuthorized(authResult.Value, now);
            repository.Update(payment);
        }

        await Task.CompletedTask;
        return Result<PaymentSummary>.Success(MapToSummary(payment));
    }

    public async Task<Result> RefreshStatusAsync(Guid paymentId, DateTimeOffset now)
    {
        var payment = repository.FindById(paymentId);

        if (payment is null)
        {
            return Result.Failure("payment_not_found", "پرداخت پیدا نشد.");
        }

        // از ارائه‌دهنده وضعیت را می‌گیریم — نه از کلاینت.
        var statusResult = gateway.GetStatus(paymentId);

        if (!statusResult.IsSuccess)
        {
            return Result.Failure(statusResult.ErrorCode!, statusResult.ErrorMessage!);
        }

        var result = statusResult.Value switch
        {
            GatewayStatus.Settled => payment.MarkSettled(now),
            GatewayStatus.Failed => payment.MarkFailed(now),
            _ => Result.Success()
        };

        if (!result.IsSuccess)
        {
            return Result.Failure(result.ErrorCode!, result.ErrorMessage!);
        }

        repository.Update(payment);
        await Task.CompletedTask;
        return Result.Success();
    }

    public async Task<Result> VoidAsync(Guid paymentId, DateTimeOffset now)
    {
        var payment = repository.FindById(paymentId);

        if (payment is null)
        {
            return Result.Failure("payment_not_found", "پرداخت پیدا نشد.");
        }

        // با ارائه‌دهنده void می‌کنیم.
        var voidResult = gateway.Void(paymentId);

        if (!voidResult.IsSuccess)
        {
            return Result.Failure(voidResult.ErrorCode!, voidResult.ErrorMessage!);
        }

        var result = payment.MarkFailed(now);

        if (!result.IsSuccess)
        {
            return Result.Failure(result.ErrorCode!, result.ErrorMessage!);
        }

        repository.Update(payment);
        await Task.CompletedTask;
        return Result.Success();
    }

    private static PaymentSummary MapToSummary(Payment payment) =>
        new(payment.Id, payment.OrderId, payment.Amount, payment.Currency,
            (int)payment.Status, payment.CreatedAt);
}

/// <summary>
/// رابط مخزن برای پرداخت‌ها.
/// </summary>
/// <remarks>
/// در Application تعریف می‌شود تا لایه اپلیکیشن به Infrastructure وابسته نباشد.
/// لایه Infrastructure این رابط را پیاده‌سازی می‌کند.
/// </remarks>
public interface IPaymentRepository
{
    Payment? FindById(Guid id);
    void Add(Payment payment);
    void Update(Payment payment);
}

/// <summary>
/// رابط درگاه پرداخت.
/// </summary>
/// <remarks>
/// این رابط توسط ارائه‌دهنده واقعی (Stripe، PayPal و...) پیاده‌سازی می‌شود.
/// تست‌ها از یک fake استفاده می‌کنند.
/// </remarks>
public interface IPaymentGateway
{
    /// <summary>
    /// پرداخت را مجاز می‌کند (authorize).
    /// </summary>
    Result<string> Authorize(Guid paymentId, decimal amount, string currency);

    /// <summary>
    /// وضعیت پرداخت را از ارائه‌دهنده می‌گیرد.
    /// </summary>
    Result<GatewayStatus> GetStatus(Guid paymentId);

    /// <summary>
    /// پرداخت را void می‌کند (لغو قبل از capture).
    /// </summary>
    Result Void(Guid paymentId);
}

public enum GatewayStatus
{
    Pending = 0,
    Authorized = 1,
    Settled = 2,
    Failed = 3,
    Refunded = 4,
}
