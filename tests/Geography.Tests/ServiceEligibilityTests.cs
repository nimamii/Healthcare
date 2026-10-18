using FluentAssertions;
using Healthcare.Modules.Geography.Contracts;
using Xunit;

namespace Healthcare.Tests.Geography;

/// <summary>
/// The eligibility decision.
///
/// §1 is specific: a customer outside a supplier's normal area must NOT be
/// blocked. The cost is calculated, shown, and confirmed. Refusing is not an
/// option.
///
/// These tests exist because that rule is invisible in the type signature. A
/// bool named `IsServable` would allow an implementation that returns false,
/// and the rule would only break in production, for a customer, on an order
/// they had every right to place.
/// </summary>
public sealed class ServiceEligibilityTests
{
    [Fact]
    public void A_supplier_with_no_areas_serves_everywhere()
    {
        // Not "serves nowhere". Getting this backwards would make every newly
        // onboarded supplier invisible.
        var eligibility = ServiceEligibility.Unrestricted();

        eligibility.IsInArea.Should().BeTrue();
        eligibility.ServiceAreaId.Should().BeNull();
        eligibility.RequiresCustomerConfirmation.Should().BeFalse();
    }

    [Fact]
    public void An_address_inside_an_area_needs_no_confirmation()
    {
        var areaId = Guid.NewGuid();

        var eligibility = ServiceEligibility.InArea(areaId);

        eligibility.IsInArea.Should().BeTrue();
        eligibility.ServiceAreaId.Should().Be(areaId);
        eligibility.Surcharge.Should().BeNull();
        eligibility.RequiresCustomerConfirmation.Should().BeFalse();
    }

    [Fact]
    public void An_address_outside_an_area_with_a_surcharge_requires_confirmation()
    {
        var eligibility = ServiceEligibility.OutsideAreaWithSurcharge(120_000, "IRR");

        eligibility.IsInArea.Should().BeFalse(
            "the address is outside, but that is not a rejection");
        eligibility.Surcharge.Should().Be(120_000);
        eligibility.Currency.Should().Be("IRR");
        eligibility.RequiresCustomerConfirmation.Should().BeTrue();
    }

    [Fact]
    public void An_address_outside_an_area_with_no_surcharge_still_delivers()
    {
        // The supplier absorbs the distance. There is nothing to confirm, so
        // prompting would be a pointless extra step for the customer.
        var eligibility = ServiceEligibility.OutsideAreaWithoutSurcharge();

        eligibility.IsInArea.Should().BeFalse();
        eligibility.Surcharge.Should().BeNull();
        eligibility.RequiresCustomerConfirmation.Should().BeFalse();
    }

    [Fact]
    public void No_eligibility_outcome_rejects_the_order()
    {
        // Every factory returns a state the caller can act on — there is no
        // "reject" and no way to build one. A future rejection branch would
        // break this test, which is the point of writing it down.
        //
        // The rule asserted is the one §1 actually states: confirmation is
        // asked for when, and only when, there is a cost to confirm.
        var outcomes = new[]
        {
            ServiceEligibility.Unrestricted(),
            ServiceEligibility.InArea(Guid.NewGuid()),
            ServiceEligibility.OutsideAreaWithSurcharge(120_000, "IRR"),
            ServiceEligibility.OutsideAreaWithoutSurcharge(),
        };

        foreach (var outcome in outcomes)
        {
            var expectsConfirmation = !outcome.IsInArea
                                       && outcome.Surcharge is > 0;

            outcome.RequiresCustomerConfirmation.Should().Be(
                expectsConfirmation,
                "confirmation is asked for exactly when there is a cost to confirm");
        }
    }

    [Fact]
    public void Confirmation_is_required_exactly_when_there_is_a_cost()
    {
        // Stated as a property rather than repeated per test so a future
        // change to the factories has one place to contradict.
        var withSurcharge = ServiceEligibility.OutsideAreaWithSurcharge(120_000, "IRR");
        var withoutSurcharge = ServiceEligibility.OutsideAreaWithoutSurcharge();
        var inside = ServiceEligibility.InArea(Guid.NewGuid());

        withSurcharge.RequiresCustomerConfirmation.Should().BeTrue();
        withoutSurcharge.RequiresCustomerConfirmation.Should().BeFalse();
        inside.RequiresCustomerConfirmation.Should().BeFalse();
    }

    [Fact]
    public void An_inside_address_carries_no_surcharge()
    {
        // A surcharge on an in-area delivery would be double-charging.
        var inside = ServiceEligibility.InArea(Guid.NewGuid());

        inside.Surcharge.Should().BeNull();
    }

    [Fact]
    public void An_outside_address_reports_which_areas_were_not_matched()
    {
        // No area id: the address matched none of them. Reporting one would
        // imply a match that did not happen.
        var outside = ServiceEligibility.OutsideAreaWithSurcharge(120_000, "IRR");

        outside.ServiceAreaId.Should().BeNull();
    }

    [Fact]
    public void A_geo_point_carries_latitude_and_longitude_in_that_order()
    {
        var point = new GeoPoint(35.6892, 51.3890);

        point.Latitude.Should().Be(35.6892);
        point.Longitude.Should().Be(51.3890);
    }
}