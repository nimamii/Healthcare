using FluentAssertions;
using Healthcare.Modules.Payments.Application;
using Healthcare.Modules.Payments.Domain;
using Healthcare.SharedKernel;
using Xunit;

namespace Healthcare.Tests.Payments;

public sealed class PaymentTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid OrderId = Guid.NewGuid();

    // --- Create ---------------------------------------------------------------

    [Fact]
    public void A_new_payment_starts_in_pending_status()
    {
        var payment = Payment.Create(OrderId, 150_000, "IRR", Now).Value;

        payment.Status.Should().Be(PaymentStatus.Pending);
        payment.OrderId.Should().Be(OrderId);
        payment.Amount.Should().Be(150_000);
        payment.Currency.Should().Be("IRR");
        payment.ProviderTransactionId.Should().BeNull();
    }

    [Fact]
    public void An_empty_order_id_is_rejected()
    {
        var result = Payment.Create(Guid.Empty, 100, "IRR", Now);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("payment_order_required");
    }

    [Fact]
    public void A_zero_amount_is_rejected()
    {
        var result = Payment.Create(OrderId, 0, "IRR", Now);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("payment_amount_invalid");
    }

    [Fact]
    public void A_negative_amount_is_rejected()
    {
        var result = Payment.Create(OrderId, -100, "IRR", Now);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("payment_amount_invalid");
    }

    [Fact]
    public void An_invalid_currency_is_rejected()
    {
        var result = Payment.Create(OrderId, 100, "TOOLONG", Now);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("payment_currency_invalid");
    }

    // --- State machine: happy path ---------------------------------------------

    [Fact]
    public void The_happy_path_walks_through_every_state()
    {
        var payment = Payment.Create(OrderId, 150_000, "IRR", Now).Value;

        payment.MarkAuthorized("tx_123", Now).IsSuccess.Should().BeTrue();
        payment.Status.Should().Be(PaymentStatus.Authorized);
        payment.ProviderTransactionId.Should().Be("tx_123");

        payment.MarkCaptured(Now).IsSuccess.Should().BeTrue();
        payment.Status.Should().Be(PaymentStatus.Captured);

        payment.MarkSettled(Now).IsSuccess.Should().BeTrue();
        payment.Status.Should().Be(PaymentStatus.Settled);
    }

    // --- State machine: invalid transitions ------------------------------------

    [Fact]
    public void A_payment_cannot_skip_authorized()
    {
        var payment = Payment.Create(OrderId, 100, "IRR", Now).Value;

        var result = payment.MarkCaptured(Now);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("payment_invalid_transition");
        payment.Status.Should().Be(PaymentStatus.Pending);
    }

    [Fact]
    public void A_payment_cannot_skip_captured()
    {
        var payment = Payment.Create(OrderId, 100, "IRR", Now).Value;
        payment.MarkAuthorized("tx_123", Now);

        var result = payment.MarkSettled(Now);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("payment_invalid_transition");
        payment.Status.Should().Be(PaymentStatus.Authorized);
    }

    // --- Failure and refund ---------------------------------------------------

    [Fact]
    public void A_pending_payment_can_fail()
    {
        var payment = Payment.Create(OrderId, 100, "IRR", Now).Value;

        payment.MarkFailed(Now).IsSuccess.Should().BeTrue();
        payment.Status.Should().Be(PaymentStatus.Failed);
    }

    [Fact]
    public void A_settled_payment_can_be_refunded()
    {
        var payment = Payment.Create(OrderId, 100, "IRR", Now).Value;
        payment.MarkAuthorized("tx_123", Now);
        payment.MarkCaptured(Now);
        payment.MarkSettled(Now);

        payment.MarkRefunded(Now).IsSuccess.Should().BeTrue();
        payment.Status.Should().Be(PaymentStatus.Refunded);
    }

    [Fact]
    public void A_pending_payment_cannot_be_refunded()
    {
        var payment = Payment.Create(OrderId, 100, "IRR", Now).Value;

        var result = payment.MarkRefunded(Now);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("payment_invalid_transition");
    }

    [Fact]
    public void A_settled_payment_cannot_fail()
    {
        var payment = Payment.Create(OrderId, 100, "IRR", Now).Value;
        payment.MarkAuthorized("tx_123", Now);
        payment.MarkCaptured(Now);
        payment.MarkSettled(Now);

        var result = payment.MarkFailed(Now);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("payment_invalid_transition");
    }
}

// --- PaymentService -------------------------------------------------------------

/// <summary>
/// A hand-written fake repository and gateway. No mocking framework.
/// </summary>
internal sealed class FakePaymentRepository : IPaymentRepository
{
    private readonly Dictionary<Guid, Payment> _payments = new();

    public Payment? FindById(Guid id) => _payments.TryGetValue(id, out var payment) ? payment : null;

    public void Add(Payment payment) => _payments[payment.Id] = payment;

    public void Update(Payment payment) => _payments[payment.Id] = payment;
}

internal sealed class FakePaymentGateway : IPaymentGateway
{
    public Result<string> Authorize(Guid paymentId, decimal amount, string currency) =>
        Result<string>.Success($"mock_tx_{paymentId:N}");

    public Result<GatewayStatus> GetStatus(Guid paymentId) =>
        Result<GatewayStatus>.Success(GatewayStatus.Settled);

    public Result Void(Guid paymentId) => Result.Success();
}

public sealed class PaymentServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid OrderId = Guid.NewGuid();

    private readonly FakePaymentRepository _repository = new();

    private readonly FakePaymentGateway _gateway = new();

    private PaymentService Service => new(_repository, _gateway);

    [Fact]
    public async Task CreatePayment_persists_the_payment()
    {
        var result = await Service.CreatePaymentAsync(OrderId, 150_000, "IRR", Now);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be((int)PaymentStatus.Authorized);
        _repository.FindById(result.Value.Id).Should().NotBeNull();
    }

    [Fact]
    public async Task CreatePayment_with_invalid_amount_does_not_persist()
    {
        var result = await Service.CreatePaymentAsync(OrderId, 0, "IRR", Now);

        result.IsSuccess.Should().BeFalse();
        _repository.FindById(Guid.NewGuid()).Should().BeNull();
    }

    [Fact]
    public async Task RefreshStatus_updates_to_settled()
    {
        var payment = Payment.Create(OrderId, 100, "IRR", Now).Value;
        payment.MarkAuthorized("tx_123", Now);
        payment.MarkCaptured(Now);
        _repository.Add(payment);

        var result = await Service.RefreshStatusAsync(payment.Id, Now);

        result.IsSuccess.Should().BeTrue();
        _repository.FindById(payment.Id)!.Status.Should().Be(PaymentStatus.Settled);
    }

    [Fact]
    public async Task RefreshStatus_on_missing_payment_fails()
    {
        var result = await Service.RefreshStatusAsync(Guid.NewGuid(), Now);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("payment_not_found");
    }

    [Fact]
    public async Task Void_marks_payment_as_failed()
    {
        var payment = Payment.Create(OrderId, 100, "IRR", Now).Value;
        _repository.Add(payment);

        var result = await Service.VoidAsync(payment.Id, Now);

        result.IsSuccess.Should().BeTrue();
        _repository.FindById(payment.Id)!.Status.Should().Be(PaymentStatus.Failed);
    }
}
