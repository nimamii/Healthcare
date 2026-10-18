namespace Healthcare.Modules.Identity.Application;

/// <summary>
/// Persists refresh token hashes. The interface lives in Application so the
/// auth service can be tested without a database.
/// </summary>
public interface IRefreshTokenStore
{
    Task StoreAsync(Guid userId, string tokenHash, DateTimeOffset expiresAt, CancellationToken ct);

    /// <summary>
    /// Looks up a hash. Returns null when unknown. A previously-used token
    /// is still found here — that is how reuse is detected (see
    /// <see cref="RefreshTokenValidation.IsReused"/>).
    /// </summary>
    Task<RefreshTokenRecord?> FindAsync(string tokenHash, CancellationToken ct);

    /// <summary>Revokes a single token (logout).</summary>
    Task RevokeAsync(string tokenHash, CancellationToken ct);

    /// <summary>
    /// Revokes every token for a user. Called when a token is replayed: a
    /// replay means the token leaked, so the safe assumption is that the
    /// attacker may have more than one.
    /// </summary>
    Task RevokeAllForUserAsync(Guid userId, CancellationToken ct);
}

public readonly record struct RefreshTokenRecord(
    Guid UserId,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt);
