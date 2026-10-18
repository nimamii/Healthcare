using FluentAssertions;
using Healthcare.Modules.Inventory.Domain;
using Healthcare.SharedKernel;
using Xunit;

namespace Healthcare.Tests.Inventory;

/// <summary>
/// Stock arithmetic.
///
/// This is the §39 high-risk area: a wrong answer here means selling a product
/// that does not exist. The tests concentrate on the boundaries — what happens
/// at zero, what happens on a repeat, and what a supplier's sync does to stock
/// an order is holding.
/// </summary>
public sealed class StockItemTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid SkuId = Guid.NewGuid();

    private static readonly Guid SupplierId = Guid.NewGuid();

    private static StockItem WithStock(int available, int reserved = 0)
    {
        var item = StockItem.Create(SkuId, SupplierId, available, Now).Value;

        if (reserved > 0)
        {
            item.Reserve(reserved, Now);
        }

        return item;
    }

    // --- Creation ------------------------------------------------------------

    [Fact]
    public void New_stock_starts_available_and_nothing_reserved()
    {
        var item = StockItem.Create(SkuId, SupplierId, 100, Now).Value;

        item.AvailableQuantity.Should().Be(100);
        item.ReservedQuantity.Should().Be(0);
        item.TotalQuantity.Should().Be(100);
        item.Version.Should().Be(1);
    }

    [Fact]
    public void A_missing_sku_is_rejected()
    {
        StockItem.Create(Guid.Empty, SupplierId, 10, Now)
            .ErrorCode.Should().Be("stock_sku_required");
    }

    [Fact]
    public void A_missing_supplier_is_rejected()
    {
        StockItem.Create(SkuId, Guid.Empty, 10, Now)
            .ErrorCode.Should().Be("stock_supplier_required");
    }

    [Fact]
    public void Negative_initial_stock_is_rejected()
    {
        StockItem.Create(SkuId, SupplierId, -1, Now)
            .ErrorCode.Should().Be("stock_negative_initial");
    }

    // --- Reservation --------------------------------------------------------

    [Fact]
    public void Reserving_moves_stock_from_available_to_reserved()
    {
        var item = WithStock(10);

        item.Reserve(3, Now).IsSuccess.Should().BeTrue();

        item.AvailableQuantity.Should().Be(7);
        item.ReservedQuantity.Should().Be(3);

        // The total must not change. Reserving relocates stock, it does not
        // create or destroy it.
        item.TotalQuantity.Should().Be(10);
    }

    [Fact]
    public void Reserving_the_entire_stock_is_allowed()
    {
        var item = WithStock(10);

        item.Reserve(10, Now).IsSuccess.Should().BeTrue();

        item.AvailableQuantity.Should().Be(0);
        item.ReservedQuantity.Should().Be(10);
    }

    [Fact]
    public void Reserving_more_than_available_is_refused_and_changes_nothing()
    {
        var item = WithStock(10);

        var result = item.Reserve(11, Now);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("insufficient_stock");

        // The important part: a refused reservation must leave the item
        // untouched. Silently clamping would let a caller believe the whole
        // quantity is held when it is not.
        item.AvailableQuantity.Should().Be(10);
        item.ReservedQuantity.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Reserving_a_non_positive_quantity_is_rejected(int quantity)
    {
        var item = WithStock(10);

        item.Reserve(quantity, Now).ErrorCode.Should().Be("reserve_non_positive");
    }

    [Fact]
    public void Repeated_reservations_accumulate()
    {
        var item = WithStock(10);

        item.Reserve(3, Now);
        item.Reserve(2, Now);

        item.AvailableQuantity.Should().Be(5);
        item.ReservedQuantity.Should().Be(5);
    }

    [Fact]
    public void Reserving_exactly_twice_beyond_stock_is_refused_on_the_second_call()
    {
        // Two customers, five units. The second must fail rather than take the
        // first customer's units.
        var item = WithStock(5);

        item.Reserve(5, Now).IsSuccess.Should().BeTrue();

        var second = item.Reserve(1, Now);

        second.IsFailure.Should().BeTrue();
        item.ReservedQuantity.Should().Be(5);
    }

    // --- Release ------------------------------------------------------------

    [Fact]
    public void Releasing_returns_stock_to_available()
    {
        var item = WithStock(10);
        item.Reserve(4, Now);

        item.Release(4, Now).IsSuccess.Should().BeTrue();

        item.AvailableQuantity.Should().Be(10);
        item.ReservedQuantity.Should().Be(0);
    }

    [Fact]
    public void Releasing_more_than_reserved_is_clamped_not_negative()
    {
        // A retried release after a partial failure is legitimate. Deducting
        // more than was held would push AvailableQuantity above the total and
        // invent stock that never existed.
        var item = WithStock(10);
        item.Reserve(3, Now);

        item.Release(5, Now).IsSuccess.Should().BeTrue();

        item.AvailableQuantity.Should().Be(10);
        item.ReservedQuantity.Should().Be(0);
    }

    [Fact]
    public void Releasing_when_nothing_is_reserved_fails()
    {
        var item = WithStock(10);

        item.Release(1, Now).ErrorCode.Should().Be("release_not_reserved");
    }

    [Fact]
    public void A_repeated_release_does_not_double_the_stock()
    {
        // The retry case from §6. Release, then release again: the second must
        // fail and the total must stay at 10.
        var item = WithStock(10);
        item.Reserve(4, Now);

        item.Release(4, Now);
        item.Release(4, Now).IsFailure.Should().BeTrue();

        item.AvailableQuantity.Should().Be(10);
        item.TotalQuantity.Should().Be(10);
    }

    // --- Deduct -------------------------------------------------------------

    [Fact]
    public void Deducting_consumes_reserved_stock_permanently()
    {
        var item = WithStock(10);
        item.Reserve(4, Now);

        item.Deduct(4, Now).IsSuccess.Should().BeTrue();

        item.AvailableQuantity.Should().Be(6);
        item.ReservedQuantity.Should().Be(0);

        // Four units physically left, so the total drops.
        item.TotalQuantity.Should().Be(6);
    }

    [Fact]
    public void Deducting_more_than_reserved_is_clamped()
    {
        var item = WithStock(10);
        item.Reserve(2, Now);

        item.Deduct(5, Now).IsSuccess.Should().BeTrue();

        item.ReservedQuantity.Should().Be(0);
        item.AvailableQuantity.Should().Be(8);
    }

    [Fact]
    public void A_repeated_deduction_does_not_double_charge()
    {
        var item = WithStock(10);
        item.Reserve(3, Now);

        item.Deduct(3, Now);
        item.Deduct(3, Now).IsFailure.Should().BeTrue();

        item.TotalQuantity.Should().Be(7);
    }

    [Fact]
    public void Deducting_when_nothing_is_reserved_fails()
    {
        var item = WithStock(10);

        item.Deduct(1, Now).ErrorCode.Should().Be("deduct_not_reserved");
    }

    // --- Adjust -------------------------------------------------------------

    [Fact]
    public void Adjusting_sets_the_available_quantity()
    {
        var item = WithStock(10);

        item.Adjust(25, Now).IsSuccess.Should().BeTrue();

        item.AvailableQuantity.Should().Be(25);
    }

    [Fact]
    public void Adjusting_to_zero_is_allowed()
    {
        // A supplier running out is not an error.
        var item = WithStock(10);

        item.Adjust(0, Now).IsSuccess.Should().BeTrue();

        item.AvailableQuantity.Should().Be(0);
    }

    [Fact]
    public void Adjusting_negative_is_rejected()
    {
        var item = WithStock(10);

        item.Adjust(-1, Now).ErrorCode.Should().Be("adjust_negative");
    }

    [Fact]
    public void Adjusting_does_not_touch_reserved_stock()
    {
        // A correction to physical stock cannot move units an order is
        // holding, or the reservation stops being satisfiable.
        var item = WithStock(10);
        item.Reserve(6, Now);

        item.Adjust(100, Now);

        item.AvailableQuantity.Should().Be(100);
        item.ReservedQuantity.Should().Be(6);
    }

    // --- Invariants ---------------------------------------------------------

    [Fact]
    public void No_operation_ever_makes_available_negative()
    {
        var item = WithStock(10);

        item.Reserve(4, Now);
        item.Reserve(4, Now);   // refused, 2 remain
        item.Reserve(2, Now);   // exact
        item.Reserve(1, Now);   // refused

        item.AvailableQuantity.Should().Be(0);
        item.AvailableQuantity.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void A_full_reserve_release_cycle_preserves_the_total()
    {
        var item = WithStock(50);

        item.Reserve(20, Now);
        item.Release(20, Now);

        item.TotalQuantity.Should().Be(50);
        item.AvailableQuantity.Should().Be(50);
    }

    [Fact]
    public void A_full_reserve_deduct_cycle_lowers_the_total_by_exactly_the_quantity()
    {
        var item = WithStock(50);

        item.Reserve(20, Now);
        item.Deduct(20, Now);

        item.TotalQuantity.Should().Be(30);
    }

    [Fact]
    public void Interleaved_reservations_and_releases_stay_consistent()
    {
        var item = WithStock(10);

        item.Reserve(4, Now);   // 6 available, 4 reserved
        item.Release(2, Now);   // 8 available, 2 reserved
        item.Reserve(5, Now);   // 3 available, 7 reserved
        item.Deduct(7, Now);    // 3 available, 0 reserved

        item.AvailableQuantity.Should().Be(3);
        item.ReservedQuantity.Should().Be(0);
        item.TotalQuantity.Should().Be(3);
    }
}