using FluentAssertions;
using Healthcare.Modules.Geography.Domain;
using Healthcare.SharedKernel;
using Xunit;

namespace Healthcare.Tests.Geography;

/// <summary>
/// Service-area construction rules.
/// </summary>
/// <remarks>
/// The polygon checks here are structural, not geometric. Full validity —
/// self-intersection and the rest — belongs to PostGIS, which owns the
/// authoritative definition and rejects bad geometry at write time. These
/// tests cover what the domain can decide on its own: the mistakes a caller
/// actually makes when building a request.
/// </remarks>
public sealed class ServiceAreaTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid SupplierId = Guid.NewGuid();

    // A triangle over central Tehran, closed as WKT requires.
    private const string Triangle =
        "POLYGON((51.30 35.60, 51.45 35.60, 51.45 35.75, 51.30 35.60))";

    private static readonly string MultiPolygon =
        "MULTIPOLYGON(((51.30 35.60, 51.45 35.60, 51.45 35.75, 51.30 35.60)))";

    private static Result<ServiceArea> Create(
        string name = "تهران مرکزی",
        string polygon = Triangle,
        decimal? surcharge = null,
        string? currency = null) =>
        ServiceArea.Create(SupplierId, name, polygon, surcharge, currency, Now);

    // --- Required fields -----------------------------------------------------

    [Fact]
    public void A_supplier_is_required()
    {
        ServiceArea.Create(Guid.Empty, "تهران", Triangle, null, null, Now)
            .ErrorCode.Should().Be("service_area_supplier_required");
    }

    [Fact]
    public void A_name_is_required()
    {
        Create(name: "   ").ErrorCode.Should().Be("service_area_name_required");
    }

    [Fact]
    public void A_name_longer_than_200_characters_is_rejected()
    {
        Create(name: new string('ن', 201))
            .ErrorCode.Should().Be("service_area_name_too_long");
    }

    [Fact]
    public void A_name_is_trimmed()
    {
        Create(name: "  تهران مرکزی  ").Value.Name.Should().Be("تهران مرکزی");
    }

    // --- Polygon structure ---------------------------------------------------

    [Fact]
    public void A_polygon_is_required()
    {
        Create(polygon: "  ")
            .ErrorCode.Should().Be("service_area_polygon_required");
    }

    [Fact]
    public void A_point_is_rejected_because_it_is_not_a_polygon()
    {
        // The most likely caller mistake: passing a single coordinate pair.
        Create(polygon: "POINT(51.30 35.60)")
            .ErrorCode.Should().Be("service_area_polygon_invalid");
    }

    [Fact]
    public void A_linestring_is_rejected()
    {
        Create(polygon: "LINESTRING(51.30 35.60, 51.45 35.75)")
            .ErrorCode.Should().Be("service_area_polygon_invalid");
    }

    [Fact]
    public void A_truncated_polygon_is_rejected()
    {
        // Unbalanced parentheses: the shape a partial request body produces.
        Create(polygon: "POLYGON((51.30 35.60, 51.45 35.60")
            .ErrorCode.Should().Be("service_area_polygon_unbalanced");
    }

    [Fact]
    public void A_polygon_with_no_commas_is_rejected()
    {
        Create(polygon: "POLYGON((51.30 35.60 51.45 35.75 51.30 35.60))")
            .ErrorCode.Should().Be("service_area_polygon_no_vertices");
    }

    [Fact]
    public void A_lowercase_polygon_is_accepted()
    {
        // PostGIS accepts either case, so rejecting one would refuse valid
        // input for no benefit.
        Create(polygon: "polygon((51.30 35.60, 51.45 35.60, 51.45 35.75, 51.30 35.60))")
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void A_multipolygon_is_accepted()
    {
        Create(polygon: MultiPolygon).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void The_stored_polygon_is_trimmed()
    {
        Create(polygon: $"  {Triangle}  ").Value.PolygonWkt.Should().Be(Triangle);
    }

    // --- Surcharge -----------------------------------------------------------

    [Fact]
    public void An_area_without_a_surcharge_is_allowed()
    {
        var area = Create(surcharge: null).Value;

        area.OutOfAreaSurcharge.Should().BeNull();
        area.OutOfAreaCurrency.Should().BeNull();
    }

    [Fact]
    public void A_negative_surcharge_is_rejected()
    {
        Create(surcharge: -1000)
            .ErrorCode.Should().Be("service_area_surcharge_negative");
    }

    [Fact]
    public void A_surcharge_without_a_currency_is_rejected()
    {
        // Shown at checkout with nothing to attach it to. Better refused here,
        // where the supplier who typed it can be told.
        Create(surcharge: 50_000, currency: null)
            .ErrorCode.Should().Be("service_area_currency_required");
    }

    [Fact]
    public void A_currency_that_is_not_three_letters_is_rejected()
    {
        Create(surcharge: 50_000, currency: "IR")
            .ErrorCode.Should().Be("service_area_currency_invalid");

        Create(surcharge: 50_000, currency: "IRRR")
            .ErrorCode.Should().Be("service_area_currency_invalid");
    }

    [Fact]
    public void A_currency_containing_digits_is_rejected()
    {
        Create(surcharge: 50_000, currency: "IR1")
            .ErrorCode.Should().Be("service_area_currency_invalid");
    }

    [Fact]
    public void A_currency_is_normalised_to_uppercase()
    {
        Create(surcharge: 50_000, currency: "irr").Value.OutOfAreaCurrency.Should().Be("IRR");
    }

    [Fact]
    public void A_zero_surcharge_with_a_currency_is_accepted()
    {
        // Zero with an explicit currency is a deliberate "no extra charge",
        // which is different from "not configured".
        var area = Create(surcharge: 0, currency: "IRR").Value;

        area.OutOfAreaSurcharge.Should().Be(0);
        area.OutOfAreaCurrency.Should().Be("IRR");
    }

    // --- Lifecycle -----------------------------------------------------------

    [Fact]
    public void A_new_area_is_active()
    {
        Create().Value.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Deactivation_preserves_the_area_rather_than_deleting_it()
    {
        // Orders reference these rows. Deleting one would either break the
        // reference or cascade away the history §22 requires.
        var area = Create().Value;

        area.Deactivate(Now);

        area.IsActive.Should().BeFalse();
        area.UpdatedAt.Should().Be(Now);
        area.PolygonWkt.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Reactivation_restores_the_active_flag()
    {
        var area = Create().Value;

        area.Deactivate(Now);
        area.Reactivate(Now);

        area.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Updating_replaces_the_details_and_stamps_the_time()
    {
        var area = Create().Value;
        var later = Now.AddHours(2);

        area.UpdateDetails("تهران و کرج", MultiPolygon, 75_000, "IRR", later);

        area.Name.Should().Be("تهران و کرج");
        area.PolygonWkt.Should().Be(MultiPolygon);
        area.OutOfAreaSurcharge.Should().Be(75_000);
        area.UpdatedAt.Should().Be(later);
    }

    [Fact]
    public void A_failed_result_cannot_yield_a_value()
    {
        var result = Create(name: "");

        var act = () => result.Value;

        act.Should().Throw<InvalidOperationException>();
    }
}