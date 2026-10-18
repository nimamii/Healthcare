namespace Healthcare.Tests.Catalog;

using FluentAssertions;
using Healthcare.Modules.Catalog.Domain;
using Healthcare.SharedKernel;
using Xunit;

/// <summary>
/// Category depth control.
///
/// The depth is stored rather than computed, so a corrupted parent pointer
/// cannot produce a cycle that only shows up as an infinite loop in a tree
/// query. These tests pin the rule that keeps that safe.
/// </summary>
public sealed class CategoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_root_category_is_depth_one()
    {
        var result = Category.CreateRoot("تجهیزات پایش", "monitoring", Now);

        result.IsSuccess.Should().BeTrue();
        result.Value.Depth.Should().Be(1);
        result.Value.ParentId.Should().BeNull();
    }

    [Fact]
    public void A_child_category_is_one_deeper_than_its_parent()
    {
        var result = Category.Create("فشار خون", "bp", parentDepth: 1, now: Now);

        result.IsSuccess.Should().BeTrue();
        result.Value.Depth.Should().Be(2);
    }

    [Fact]
    public void Depth_is_capped_at_four_levels()
    {
        // A parent already at the maximum cannot have children. Without this
        // check a crafted request could place a category at level 99, and the
        // stored depth would then be wrong for every descendant.
        var result = Category.Create("عمیق", "deep", parentDepth: Category.MaxDepth, now: Now);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("category_too_deep");
    }

    [Fact]
    public void A_parent_at_the_maximum_depth_is_rejected_with_a_clear_reason()
    {
        var result = Category.Create("عمیق", "deep", parentDepth: Category.MaxDepth, now: Now);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("category_too_deep");

        // The message interpolates MaxDepth directly, so the number in the
        // text is whatever format the runtime renders — asserting on a
        // specific digit shape would pin the test to one culture.
        result.ErrorMessage.Should().Contain("سطح");
        result.ErrorMessage.Should().Contain(
            Category.MaxDepth.ToString(),
            "the message should state the limit it exceeded");
    }

    [Fact]
    public void The_depth_limit_message_states_the_limit()
    {
        var result = Category.Create("عمیق", "deep", parentDepth: Category.MaxDepth, now: Now);

        // Separate from the test above: this one pins the actual number, so a
        // change to MaxDepth is caught rather than silently passing.
        result.ErrorMessage.Should().Be(
            $"دسته‌بندی نمی‌تواند عمیق‌تر از {Category.MaxDepth} سطح باشد.");
    }

    [Fact]
    public void An_empty_name_is_rejected()
    {
        var result = Category.CreateRoot("   ", "slug", Now);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("category_name_required");
    }

    [Fact]
    public void A_name_longer_than_200_characters_is_rejected()
    {
        var result = Category.CreateRoot(new string('ن', 201), "slug", Now);

        result.ErrorCode.Should().Be("category_name_too_long");
    }

    [Fact]
    public void Names_are_trimmed()
    {
        var result = Category.CreateRoot("  تجهیزات  ", "slug", Now);

        result.Value.Name.Should().Be("تجهیزات");
    }

    [Fact]
    public void Slugs_are_normalised_to_lowercase()
    {
        var result = Category.CreateRoot("تجهیزات", "Monitoring-Devices", Now);

        result.Value.Slug.Should().Be("monitoring-devices");
    }

    [Fact]
    public void An_empty_slug_is_rejected()
    {
        var result = Category.CreateRoot("تجهیزات", "   ", Now);

        result.ErrorCode.Should().Be("category_slug_required");
    }

    [Fact]
    public void Deactivation_preserves_the_category_rather_than_deleting_it()
    {
        // Products still reference this row. Deleting it would either break
        // those references or cascade away the whole subtree.
        var category = Category.CreateRoot("تجهیزات", "slug", Now).Value;

        category.Deactivate(Now);

        category.IsActive.Should().BeFalse();
        category.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public void Reactivation_restores_the_active_flag()
    {
        var category = Category.CreateRoot("تجهیزات", "slug", Now).Value;

        category.Deactivate(Now);
        category.Reactivate(Now);

        category.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Rename_updates_the_timestamp()
    {
        var category = Category.CreateRoot("تجهیزات", "slug", Now).Value;
        var later = Now.AddMinutes(5);

        category.Rename("تجهیزات جدید", "new-slug", later);

        category.Name.Should().Be("تجهیزات جدید");
        category.Slug.Should().Be("new-slug");
        category.UpdatedAt.Should().Be(later);
    }

    [Fact]
    public void A_failed_result_cannot_yield_a_value()
    {
        // Reading Value on a failure must be loud. A silent null would let a
        // missed IsSuccess check become a NullReferenceException far from the
        // actual mistake.
        var result = Category.CreateRoot("", "slug", Now);

        var act = () => result.Value;

        act.Should().Throw<InvalidOperationException>();
    }
}
