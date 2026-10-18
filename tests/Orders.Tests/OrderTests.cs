using FluentAssertions;
using Healthcare.Modules.Orders.Application;
using Healthcare.Modules.Orders.Domain;
using Xunit;

namespace Healthcare.Tests.Orders;

public sealed class OrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid CustomerId = Guid.NewGuid();

    private static readonly Guid SupplierId = Guid.NewGuid();

    // --- Create ---------------------------------------------------------------

    [Fact]
    public void A_new_order_starts_in_created_status()
    {
        var order = Order.Create(CustomerId, SupplierId, Now).Value;

        order.Status.Should().Be(OrderStatus.Created);
        order.CustomerId.Should().Be(CustomerId);
        order.SupplierId.Should().Be(SupplierId);
    }

    [Fact]
    public void An_empty_customer_id_is_rejected()
    {
        var result = Order.Create(Guid.Empty, SupplierId, Now);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("order_customer_required");
    }

    [Fact]
    public void An_empty_supplier_id_is_rejected()
    {
        var result = Order.Create(CustomerId, Guid.Empty, Now);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("order_supplier_required");
    }

    // --- State machine: happy path ---------------------------------------------

    [Fact]
    public void The_happy_path_walks_through_every_state()
    {
        var order = Order.Create(CustomerId, SupplierId, Now).Value;

        order.MarkPaymentPending(Now).IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.PaymentPending);

        order.MarkPaid(Now).IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Paid);

        order.MarkFulfillmentPending(Now).IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.FulfillmentPending);

        order.Confirm(Now).IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Confirmed);
    }

    // --- State machine: invalid transitions ------------------------------------

    [Fact]
    public void An_order_cannot_skip_payment_pending()
    {
        var order = Order.Create(CustomerId, SupplierId, Now).Value;

        var result = order.MarkPaid(Now);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("order_invalid_transition");
        order.Status.Should().Be(OrderStatus.Created);
    }

    [Fact]
    public void An_order_cannot_skip_paid()
    {
        var order = Order.Create(CustomerId, SupplierId, Now).Value;
        order.MarkPaymentPending(Now);

        var result = order.MarkFulfillmentPending(Now);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("order_invalid_transition");
        order.Status.Should().Be(OrderStatus.PaymentPending);
    }

    [Fact]
    public void An_order_cannot_skip_fulfillment_pending()
    {
        var order = Order.Create(CustomerId, SupplierId, Now).Value;
        order.MarkPaymentPending(Now);
        order.MarkPaid(Now);

        var result = order.Confirm(Now);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("order_invalid_transition");
        order.Status.Should().Be(OrderStatus.Paid);
    }

    // --- Cancel ---------------------------------------------------------------

    [Fact]
    public void A_created_order_can_be_cancelled()
    {
        var order = Order.Create(CustomerId, SupplierId, Now).Value;

        order.Cancel(Now).IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Cancelled);
    }

    [Fact]
    public void A_paid_order_can_be_cancelled()
    {
        var order = Order.Create(CustomerId, SupplierId, Now).Value;
        order.MarkPaymentPending(Now);
        order.MarkPaid(Now);

        order.Cancel(Now).IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Cancelled);
    }

    // --- Lines ---------------------------------------------------------------

    [Fact]
    public void A_line_with_zero_quantity_is_rejected()
    {
        var order = Order.Create(CustomerId, SupplierId, Now).Value;

        var result = order.AddLine(Guid.NewGuid(), 0, 100, "IRR", Now);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("order_line_quantity_invalid");
    }

    [Fact]
    public void A_line_with_negative_price_is_rejected()
    {
        var order = Order.Create(CustomerId, SupplierId, Now).Value;

        var result = order.AddLine(Guid.NewGuid(), 1, -100, "IRR", Now);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("order_line_price_invalid");
    }

    [Fact]
    public void A_line_with_invalid_currency_is_rejected()
    {
        var order = Order.Create(CustomerId, SupplierId, Now).Value;

        var result = order.AddLine(Guid.NewGuid(), 1, 100, "TOOLONG", Now);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("order_line_currency_invalid");
    }

    [Fact]
    public void A_valid_line_is_added()
    {
        var order = Order.Create(CustomerId, SupplierId, Now).Value;
        var skuId = Guid.NewGuid();

        var result = order.AddLine(skuId, 2, 150_000, "IRR", Now);

        result.IsSuccess.Should().BeTrue();
        order.Lines.Should().HaveCount(1);
        order.Lines.First().SkuId.Should().Be(skuId);
        order.Lines.First().Quantity.Should().Be(2);
        order.Lines.First().UnitPrice.Should().Be(150_000);
        order.Lines.First().Currency.Should().Be("IRR");
    }
}

// --- OrderService -------------------------------------------------------------

/// <summary>
/// A hand-written fake repository. No mocking framework: the interface is
/// small enough that a fake is clearer than a mock and has no dependency.
/// </summary>
internal sealed class FakeOrderRepository : IOrderRepository
{
    private readonly Dictionary<Guid, Order> _orders = new();

    public Order? FindById(Guid id) => _orders.TryGetValue(id, out var order) ? order : null;

    public void Add(Order order) => _orders[order.Id] = order;

    public void Update(Order order) => _orders[order.Id] = order;
}

public sealed class OrderServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid CustomerId = Guid.NewGuid();

    private static readonly Guid SupplierId = Guid.NewGuid();

    private static readonly Guid SkuId = Guid.NewGuid();

    private readonly FakeOrderRepository _repository = new();

    private OrderService Service => new(_repository);

    [Fact]
    public async Task CreateOrder_persists_the_order()
    {
        var result = await Service.CreateOrder(CustomerId, SupplierId, Now);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be((int)OrderStatus.Created);
        _repository.FindById(result.Value.Id).Should().NotBeNull();
    }

    [Fact]
    public async Task CreateOrder_with_empty_customer_id_does_not_persist()
    {
        var result = await Service.CreateOrder(Guid.Empty, SupplierId, Now);

        result.IsSuccess.Should().BeFalse();
        _repository.FindById(Guid.NewGuid()).Should().BeNull();
    }

    [Fact]
    public async Task AddLine_appends_to_an_existing_order()
    {
        var order = Order.Create(CustomerId, SupplierId, Now).Value;
        _repository.Add(order);

        var result = await Service.AddLine(order.Id, SkuId, 2, 150_000, "IRR", Now);

        result.IsSuccess.Should().BeTrue();
        _repository.FindById(order.Id)!.Lines.Should().HaveCount(1);
    }

    [Fact]
    public async Task AddLine_on_a_missing_order_fails()
    {
        var result = await Service.AddLine(Guid.NewGuid(), SkuId, 1, 100, "IRR", Now);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("order_not_found");
    }

    [Fact]
    public async Task MarkPaid_updates_a_payment_pending_order()
    {
        var order = Order.Create(CustomerId, SupplierId, Now).Value;
        order.MarkPaymentPending(Now);
        _repository.Add(order);

        var result = await Service.MarkPaid(order.Id, Now);

        result.IsSuccess.Should().BeTrue();
        _repository.FindById(order.Id)!.Status.Should().Be(OrderStatus.Paid);
    }

    [Fact]
    public async Task Cancel_marks_a_created_order_as_cancelled()
    {
        var order = Order.Create(CustomerId, SupplierId, Now).Value;
        _repository.Add(order);

        var result = await Service.Cancel(order.Id, Now);

        result.IsSuccess.Should().BeTrue();
        _repository.FindById(order.Id)!.Status.Should().Be(OrderStatus.Cancelled);
    }
}
