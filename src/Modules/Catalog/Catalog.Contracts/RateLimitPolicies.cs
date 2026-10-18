namespace Healthcare.Modules.Catalog.Contracts;

/// <summary>
/// Rate limit policy names, declared by the module that needs them and
/// enforced by the host.
///
/// Same split as Identity: the module says which of its endpoints are
/// sensitive, the host decides what a limit means. A module cannot change the
/// enforcement behaviour by renaming its own policy.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>
    /// Catalogue writes. Stricter than the authenticated default because these
    /// are administrative operations that no legitimate user performs in bulk,
    /// and a compromised admin token should not be able to rewrite the
    /// catalogue in a loop.
    /// </summary>
    public const string CatalogAdmin = "catalog-admin";
}