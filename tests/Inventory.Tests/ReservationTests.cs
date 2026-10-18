using FluentAssertions;
using Healthcare.Modules.Inventory.Domain;
using Healthcare.SharedKernel;
using Xunit;

namespace Healthcare.Tests.Inventory;

/// <summary>
/// Reservation lifecycle.
///
/// The sweep is what keeps abandoned carts from stranding stock, so the
/// expiry rules get the most attention here.
/// </summary>
public sealed class ReservationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid OrderId = Guid.NewGuid();

    private static readonly Guid SkuId = Guid.NewGuid();

    private static Reservation Active(TimeSpan? lifetime = null) =>
        Reservation.Create(OrderId, SkuId, 5, Now, lifetime).Value;

    // --- Creation ------------------------------------------------------------

    [Fact]
    public void A_new_reservation_is_active_and_holds_the_quantity()
    {
        var reservation = Active();

        reservation.Status.Should().Be(ReservationStatus.Active);
        reservation.Quantity.Should().Be(5);
        reservation.ResolvedAt.Should().BeNull();
    }

    [Fact]
    public void The_default_lifetime_is_fifteen_minutes()
    {
        var reservation = Active();

        reservation.ExpiresAt.Should().Be(Now + Reservation.DefaultLifetime);
        Reservation.DefaultLifetime.Should().Be(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void A_custom_lifetime_is_honoured()
    {
        var reservation = Active(TimeSpan.FromHours(2));

        reservation.ExpiresAt.Should().Be(Now + TimeSpan.FromHours(2));
    }

    [Fact]
    public void A_missing_order_is_rejected()
    {
        Reservation.Create(Guid.Empty, SkuId, 1, Now)
            .ErrorCode.Should().Be("reservation_order_required");
    }

    [Fact]
    public void A_missing_sku_is_rejected()
    {
        Reservation.Create(OrderId, Guid.Empty, 1, Now)
            .ErrorCode.Should().Be("reservation_sku_required");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void A_non_positive_quantity_is_rejected(int quantity)
    {
        Reservation.Create(OrderId, SkuId, quantity, Now)
            .ErrorCode.Should().Be("reservation_non_positive");
    }

    [Fact]
    public void A_non_positive_lifetime_is_rejected()
    {
        Reservation.Create(OrderId, SkuId, 1, Now, TimeSpan.Zero)
            .ErrorCode.Should().Be("reservation_lifetime_invalid");
    }

    // --- Expiry --------------------------------------------------------------

    [Fact]
    public void An_active_reservation_is_not_expired_before_its_deadline()
    {
        var reservation = Active();

        reservation.IsExpiredAt(Now).Should().BeFalse();
        reservation.IsExpiredAt(reservation.ExpiresAt - TimeSpan.FromSeconds(1))
            .Should().BeFalse();
    }

    [Fact]
    public void An_active_reservation_is_expired_at_its_deadline()
    {
        // Exactly at the deadline counts as expired. Off-by-one here leaves
        // stock held an extra sweep cycle past its window.
        var reservation = Active();

        reservation.IsExpiredAt(reservation.ExpiresAt).Should().BeTrue();
        reservation.IsExpiredAt(reservation.ExpiresAt + TimeSpan.FromMinutes(1))
            .Should().BeTrue();
    }

    [Fact]
    public void A_resolved_reservation_is_never_expired()
    {
        // Otherwise a sweep could re-release a reservation the sweep already
        // released, returning the same stock twice.
        var reservation = Active();

        reservation.Release(Now);

        reservation.IsExpiredAt(reservation.ExpiresAt + TimeSpan.FromHours(1))
            .Should().BeFalse();
    }

    [Fact]
    public void Expiring_moves_the_status_and_records_when()
    {
        var at = Now.AddMinutes(20);
        var reservation = Active();

        reservation.Expire(at).IsSuccess.Should().BeTrue();

        reservation.Status.Should().Be(ReservationStatus.Expired);
        reservation.ResolvedAt.Should().Be(at);
    }

    // --- Release and deduct --------------------------------------------------

    [Fact]
    public void Releasing_an_active_reservation_succeeds()
    {
        var reservation = Active();

        reservation.Release(Now).IsSuccess.Should().BeTrue();

        reservation.Status.Should().Be(ReservationStatus.Released);
    }

    [Fact]
    public void Deducting_an_active_reservation_succeeds()
    {
        var reservation = Active();

        reservation.Deduct(Now).IsSuccess.Should().BeTrue();

        reservation.Status.Should().Be(ReservationStatus.Deducted);
    }

    [Theory]
    [InlineData(ReservationStatus.Released)]
    [InlineData(ReservationStatus.Deducted)]
    [InlineData(ReservationStatus.Expired)]
    public void A_resolved_reservation_cannot_be_resolved_again(ReservationStatus status)
    {
        // This is the retry guard. A paid order whose "deduct" is retried must
        // not release stock a second time.
        var reservation = Active();

        switch (status)
        {
            case ReservationStatus.Released: reservation.Release(Now); break;
            case ReservationStatus.Deducted: reservation.Deduct(Now); break;
            case ReservationStatus.Expired: reservation.Expire(Now); break;
        }

        reservation.Release(Now).IsFailure.Should().BeTrue();
        reservation.Deduct(Now).IsFailure.Should().BeTrue();
        reservation.Expire(Now).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void A_double_release_does_not_change_the_status()
    {
        var reservation = Active();

        reservation.Release(Now);
        reservation.Release(Now).IsFailure.Should().BeTrue();

        reservation.Status.Should().Be(ReservationStatus.Released);
    }

    [Fact]
    public void Release_and_expire_are_distinguishable_in_the_record()
    {
        // §23 requires the audit trail to separate "the customer cancelled"
        // from "they walked away". Same effect on stock, different history.
        var cancelled = Active();
        var abandoned = Active();

        cancelled.Release(Now);
        abandoned.Expire(Now);

        cancelled.Status.Should().Be(ReservationStatus.Released);
        abandoned.Status.Should().Be(ReservationStatus.Expired);
    }

    // --- Lifecycle ----------------------------------------------------------

    [Fact]
    public void Resolved_at_is_null_until_the_reservation_ends()
    {
        var reservation = Active();

        reservation.ResolvedAt.Should().BeNull();

        reservation.Release(Now);

        reservation.ResolvedAt.Should().Be(Now);
    }

    [Fact]
    public void Expiry_uses_the_supplied_time_not_a_clock()
    {
        // The caller passes time in so the rule is testable without a clock,
        // and so the same rule can be driven by the sweep's own time source.
        var reservation = Active(TimeSpan.FromMinutes(15));

        var atTheEdge = Now.AddMinutes(15);
        var pastIt = Now.AddMinutes(15).AddMilliseconds(1);

        reservation.IsExpiredAt(atTheEdge).Should().BeTrue();
        reservation.IsExpiredAt(pastIt).Should().BeTrue();
    }

    [Fact]
    public void Two_reservations_for_different_orders_are_independent()
    {
        // The uniqueness constraint is on (order_id, sku_id), so two customers
        // reserving the same SKU must both succeed at the domain level. The
        // database is what prevents one customer doing it twice.
        var first = Reservation.Create(Guid.NewGuid(), SkuId, 2, Now);
        var second = Reservation.Create(Guid.NewGuid(), SkuId, 3, Now);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        first.Value.Id.Should().NotBe(second.Value.Id);
    }
}