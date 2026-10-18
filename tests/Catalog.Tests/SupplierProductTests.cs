namespace Healthcare.Tests.Catalog;

using FluentAssertions;
using Healthcare.Modules.Catalog.Domain;
using Healthcare.SharedKernel;
using Xunit;

/// <summary>
/// Supplier listing validation.
///
/// The price rules matter more than they look. A zero price is not a free item
/// — it is a data-entry mistake that becomes a real order at no cost. And a
/// negative price is worse: it can make a refund look like a charge.
/// </summary>
public sealed class SupplierProductTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid SkuId = Guid.NewGuid();

    private static readonly Guid SupplierId = Guid.NewGuid();

    private static Result<SupplierProduct> Create(decimal? price, string currency = "IRR") =>
        SupplierProduct.Create(SkuId, SupplierId, "SUP-001", price, currency, Now);

    [Fact]
    public void A_listing_with_a_price_is_created()
    {
        var listing = Create(2450000).Value;

        listing.ListPrice.Should().Be(2450000m);
        listing.Currency.Should().Be("IRR");
        listing.IsActive.Should().BeTrue();
    }

    [Fact]
    public void A_null_price_means_not_published()
    {
        // Distinct from zero. A supplier that has not published a price is not
        // offering the item for free.
        var listing = Create(null).Value;

        listing.ListPrice.Should().BeNull();
    }

    [Fact]
    public void A_zero_price_is_rejected()
    {
        var result = Create(0);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("supplier_product_zero_price");
    }

    [Fact]
    public void A_negative_price_is_rejected()
    {
        var result = Create(-100);

        result.ErrorCode.Should().Be("supplier_product_negative_price");
    }

    [Fact]
    public void A_price_with_more_than_four_decimal_places_is_rejected()
    {
        // The column is numeric(18,4). Silently rounding a supplier's price
        // would make the displayed figure disagree with the stored one.
        var result = Create(1234.56789m);

        result.ErrorCode.Should().Be("supplier_product_price_precision");
    }

    [Fact]
    public void A_price_with_four_decimal_places_is_accepted()
    {
        var listing = Create(1234.5678m).Value;

        listing.ListPrice.Should().Be(1234.5678m);
    }

    [Fact]
    public void A_missing_sku_is_rejected()
    {
        var result = SupplierProduct.Create(Guid.Empty, SupplierId, "SUP-001", 100, "IRR", Now);

        result.ErrorCode.Should().Be("supplier_product_sku_required");
    }

    [Fact]
    public void A_missing_supplier_is_rejected()
    {
        var result = SupplierProduct.Create(SkuId, Guid.Empty, "SUP-001", 100, "IRR", Now);

        result.ErrorCode.Should().Be("supplier_product_supplier_required");
    }

    [Fact]
    public void An_empty_supplier_code_is_rejected()
    {
        var result = SupplierProduct.Create(SkuId, SupplierId, "   ", 100, "IRR", Now);

        result.ErrorCode.Should().Be("supplier_product_code_required");
    }

    [Fact]
    public void A_supplier_code_longer_than_128_characters_is_rejected()
    {
        var result = SupplierProduct.Create(SkuId, SupplierId, new string('S', 129), 100, "IRR", Now);

        result.ErrorCode.Should().Be("supplier_product_code_too_long");
    }

    [Fact]
    public void A_currency_longer_than_three_characters_is_rejected()
    {
        var result = Create(100, "IRRR");

        result.ErrorCode.Should().Be("supplier_product_currency_invalid");
    }

    [Fact]
    public void A_blank_currency_defaults_to_IRR()
    {
        var listing = Create(100, "   ").Value;

        listing.Currency.Should().Be("IRR");
    }

    [Fact]
    public void A_currency_is_normalised_to_uppercase()
    {
        var listing = Create(100, "usd").Value;

        listing.Currency.Should().Be("USD");
    }

    [Fact]
    public void Update_details_replaces_the_stored_values()
    {
        var listing = Create(100).Value;

        listing.UpdateDetails("SUP-002", 200, "USD", Now);

        listing.SupplierSku.Should().Be("SUP-002");
        listing.ListPrice.Should().Be(200m);
        listing.Currency.Should().Be("USD");
        listing.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public void Deactivation_is_available()
    {
        var listing = Create(100).Value;

        listing.Deactivate(Now);

        listing.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Reactivation_is_available()
    {
        var listing = Create(100).Value;

        listing.Deactivate(Now);
        listing.Reactivate(Now);

        listing.IsActive.Should().BeTrue();
    }

    [Fact]
    public void A_failed_result_cannot_yield_a_value()
    {
        var result = Create(0);

        var act = () => result.Value;

        act.Should().Throw<InvalidOperationException>();
    }
}
