namespace Healthcare.Modules.Identity.Infrastructure;

/// <summary>
/// Persistence shape for a refresh token. Distinct from the domain concept
/// because the domain only ever sees hashes and revocation state, never the
/// token itself.
/// </summary>
public class RefreshTokenEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>
    /// Set when a revoked token is presented again. A clean re-issue clears
    /// it, so the flag marks theft rather than ordinary rotation.
    /// </summary>
    public DateTimeOffset? ReusedAt { get; set; }
}
