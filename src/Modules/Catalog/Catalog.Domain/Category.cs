using Healthcare.SharedKernel;

namespace Healthcare.Modules.Catalog.Domain;

/// <summary>
/// A node in the product taxonomy: "Stethoscopes", "Wheelchairs", "MRI".
///
/// Categories form a tree, but the depth is capped. Medical equipment
/// catalogues go deep for no useful reason, and an unbounded tree means every
/// read has to guard against cycles and every UI has to handle arbitrary
/// nesting. Four levels covers the real catalogues without that.
/// </summary>
public sealed class Category
{
    /// <summary>Maximum depth. A category at the root is level 1.</summary>
    public const int MaxDepth = 4;

    private Category()
    {
        // EF Core.
    }

    private Category(Guid id, string name, string slug, Guid? parentId, int depth, DateTimeOffset now)
    {
        Id = id;
        Name = name;
        Slug = slug;
        ParentId = parentId;
        Depth = depth;
        IsActive = true;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// URL-safe identifier, unique across the catalogue. Carried as a value
    /// rather than derived, because renaming a category must not break links
    /// that already point at it.
    /// </summary>
    public string Slug { get; private set; } = string.Empty;

    public Guid? ParentId { get; private set; }

    /// <summary>1 for a root category. Denormalised to avoid a recursive read.</summary>
    public int Depth { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Creates a root category.
    ///
    /// A separate factory rather than an optional parameter: with
    /// <c>Create(name, slug, parentId, parentDepth, now)</c> a caller could pass
    /// <c>parentDepth: 3</c> alongside <c>parentId: null</c> and the depth would
    /// silently be 1. Two parameters that must agree is a shape the compiler
    /// cannot check, so the agreement is removed instead.
    /// </summary>
    public static Result<Category> CreateRoot(string name, string slug, DateTimeOffset now) =>
        Create(name, slug, parentDepth: 0, now);

    /// <summary>
    /// Creates a category under an existing parent.
    ///
    /// <paramref name="parentDepth"/> is the parent's stored depth, read by the
    /// caller from the database. It is never taken from a request: a wrong value
    /// produces a wrong depth, never a wrong parent.
    /// </summary>
    public static Result<Category> Create(
        string name,
        string slug,
        int parentDepth,
        DateTimeOffset now)
    {
        var trimmedName = name.Trim();
        var trimmedSlug = slug.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            return Result<Category>.Failure("category_name_required", "نام دسته‌بندی الزامی است.");
        }

        if (trimmedName.Length > 200)
        {
            return Result<Category>.Failure("category_name_too_long", "نام دسته‌بندی نباید بیش از ۲۰۰ کاراکتر باشد.");
        }

        if (string.IsNullOrWhiteSpace(trimmedSlug))
        {
            return Result<Category>.Failure("category_slug_required", "شناسه یکتا (slug) الزامی است.");
        }

        if (trimmedSlug.Length > 200)
        {
            return Result<Category>.Failure("category_slug_too_long", "شناسه یکتا نباید بیش از ۲۰۰ کاراکتر باشد.");
        }

        // Depth is stored rather than computed, so a corrupted parent pointer
        // cannot produce a cycle that only shows up as an infinite loop in a
        // tree query.
        var depth = parentDepth + 1;

        if (depth > MaxDepth)
        {
            return Result<Category>.Failure(
                "category_too_deep",
                $"دسته‌بندی نمی‌تواند عمیق‌تر از {MaxDepth} سطح باشد.");
        }

        if (depth < 1)
        {
            return Result<Category>.Failure("category_invalid_depth", "عمق دسته‌بندی نامعتبر است.");
        }

        return Result<Category>.Success(new Category(
            Guid.NewGuid(),
            trimmedName,
            trimmedSlug,
            parentId: null,
            depth,
            now));
    }

    public void Rename(string name, string slug, DateTimeOffset now)
    {
        Name = name.Trim();
        Slug = slug.Trim().ToLowerInvariant();
        UpdatedAt = now;
    }

    /// <summary>
    /// Deactivation rather than deletion: products still reference this row,
    /// and a catalogue that cannot show a discontinued category is worse than
    /// one that shows it greyed out.
    /// </summary>
    public void Deactivate(DateTimeOffset now)
    {
        IsActive = false;
        UpdatedAt = now;
    }

    public void Reactivate(DateTimeOffset now)
    {
        IsActive = true;
        UpdatedAt = now;
    }
}