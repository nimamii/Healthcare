namespace Healthcare.SharedKernel;

/// <summary>
/// Commits everything tracked in one module's unit of work, as a transaction.
///
/// Exists because EF Core's <c>SaveChanges</c> is per-DbContext and a module
/// must not reach for another module's context. Without a commit, a write
/// silently does nothing: <c>AddAsync</c> stages the change, the request
/// reports success, and nothing is inserted.
///
/// <para>
/// <strong>One implementation per module, keyed by name.</strong> Registering
/// <c>IUnitOfWork</c> plainly does not work: with several modules each holding
/// their own DbContext, the last registration overwrites the others and every
/// module silently commits the wrong context. That produced "committed, 0
/// row(s)" — the write reached the database layer and was discarded there,
/// which looks like a database problem and is not one.
/// </para>
///
/// <para>
/// Commits at this boundary also keep later work honest. In Inventory, where
/// overselling must be prevented (§6, §39), "decrement available quantity and
/// insert the reservation row" has to be one atomic operation — not two
/// commits that a failure could separate.
/// </para>
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Persists all pending changes for this module. Returns the number of
    /// affected rows, which is zero when nothing was staged.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken ct);
}

/// <summary>
/// Keys for <see cref="IUnitOfWork"/> registrations.
///
/// Every module registers its own implementation against
/// <c>UnitOfWorkKeys.For("module-name")</c> and injects it by the same key, so
/// nothing collides. A module name that is not registered fails loudly at
/// startup instead of quietly committing another module's context.
/// </summary>
public static class UnitOfWorkKeys
{
    public const string Identity = "identity";
    public const string Audit = "audit";
    public const string Catalog = "catalog";
    public const string Inventory = "inventory";
    public const string Geography = "geography";
    public const string Orders = "orders";
    public const string Payments = "payments";

    /// <summary>
    /// The key a module owning its own tables must register under.
    ///
    /// The value is deliberately lowercase: schema names are lowercase, and
    /// having one spelling for both keeps a mismatch visible. A module that
    /// uses its PascalCase name here while registering under a different
    /// spelling fails to resolve — loudly, at startup, rather than quietly
    /// committing another module's context.
    /// </summary>
    public static string For(string moduleName) => moduleName.ToLowerInvariant();
}