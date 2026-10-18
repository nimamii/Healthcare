namespace Healthcare.Modules.Identity.Contracts;

/// <summary>
/// Rate limit policy names, declared by the module that needs them and
/// enforced by the host.
///
/// The split keeps the boundary honest: Identity says "these endpoints are
/// brute-force sensitive", the host says "this is what a limit means". A
/// module naming its own policy cannot accidentally change the enforcement
/// behaviour, and the host does not need to know which module owns what.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>
    /// Strict per-IP limit for unauthenticated auth endpoints. Kept low
    /// because every legitimate caller makes at most a handful of these
    /// calls, while a credential-stuffing run makes thousands.
    /// </summary>
    public const string Authentication = "identity-auth";
}
