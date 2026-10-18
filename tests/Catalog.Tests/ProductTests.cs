namespace Healthcare.Tests.Catalog;

using FluentAssertions;
using Healthcare.Modules.Catalog.Domain;
using Healthcare.SharedKernel;
using Xunit;

public sealed class ProductTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid CategoryId = Guid.NewGuid();

    private static Result<Product> Create(
        string name = "دستگاه فشار خون",
        string slug = "bp-monitor",
        Guid? categoryId = null) =>
        Product.Create(
            name,
            slug,
            "توضیحات",
            categoryId ?? CategoryId,
            requiresPrescription: false,
            Now);

    [Fact]
    public void A_product_is_created_inside_its_category()
    {
        var product = Create().Value;

        product.CategoryId.Should().Be(CategoryId);
        product.Name.Should().Be("دستگاه فشار خون");
        product.IsActive.Should().BeTrue();
    }

    [Fact]
    public void An_empty_name_is_rejected()
    {
        var result = Create(name: "  ");

        result.ErrorCode.Should().Be("product_name_required");
    }

    [Fact]
    public void A_name_longer_than_300_characters_is_rejected()
    {
        var result = Create(name: new string('ن', 301));

        result.ErrorCode.Should().Be("product_name_too_long");
    }

    [Fact]
    public void An_empty_slug_is_rejected()
    {
        var result = Create(slug: "   ");

        result.ErrorCode.Should().Be("product_slug_required");
    }

    [Fact]
    public void A_missing_category_is_rejected()
    {
        var result = Create(categoryId: Guid.Empty);

        result.ErrorCode.Should().Be("product_category_required");
    }

    [Fact]
    public void A_blank_description_becomes_null()
    {
        var product = Product.Create(
            "نام", "slug", "   ", CategoryId, false, Now).Value;

        product.Description.Should().BeNull();
    }

    [Fact]
    public void Slugs_are_normalised_to_lowercase()
    {
        var product = Create(slug: "BP-Monitor").Value;

        product.Slug.Should().Be("bp-monitor");
    }

    [Fact]
    public void Prescription_status_defaults_to_not_required()
    {
        var product = Create().Value;

        product.RequiresPrescription.Should().BeFalse();
    }

    [Fact]
    public void Prescription_status_can_be_set_at_creation()
    {
        var product = Product.Create(
            "مورفین", "morphine", null, CategoryId, requiresPrescription: true, Now).Value;

        product.RequiresPrescription.Should().BeTrue();
    }

    [Fact]
    public void Prescription_status_can_be_changed_later()
    {
        // A product can be reclassified without being recreated, which would
        // otherwise orphan every order line and supplier listing pointing at it.
        var product = Create().Value;
        var later = Now.AddMinutes(10);

        product.SetRequiresPrescription(true, later);

        product.RequiresPrescription.Should().BeTrue();
        product.UpdatedAt.Should().Be(later);
    }

    [Fact]
    public void Update_details_replaces_the_editable_fields()
    {
        var product = Create().Value;

        product.UpdateDetails("نام جدید", "new-slug", "توضیح جدید", CategoryId, true, Now);

        product.Name.Should().Be("نام جدید");
        product.Slug.Should().Be("new-slug");
        product.Description.Should().Be("توضیح جدید");
        product.RequiresPrescription.Should().BeTrue();
    }

    [Fact]
    public void Deactivation_is_available()
    {
        var product = Create().Value;

        product.Deactivate(Now);

        product.IsActive.Should().BeFalse();
        product.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public void Reactivation_is_available()
    {
        var product = Create().Value;

        product.Deactivate(Now);
        product.Reactivate(Now);

        product.IsActive.Should().BeTrue();
    }

    [Fact]
    public void A_failed_result_cannot_yield_a_value()
    {
        var result = Create(name: "");

        var act = () => result.Value;

        act.Should().Throw<InvalidOperationException>();
    }
}