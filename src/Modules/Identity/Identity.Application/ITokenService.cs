namespace Healthcare.Modules.Identity.Application;

/// <summary>
/// Issues and validates tokens. Abstracted so the signing strategy can
/// change without touching the auth flow — the spec's NEVER ASSUME rule
/// (§29) says document and make it swappable, which is exactly this.
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Issues a short-lived access token carrying the user's roles.
    /// Roles are read from database-derived state, never from the request.
    /// </summary>
    AccessToken IssueAccessToken(Guid userId, string email, IEnumerable<string> roles);

    /// <summary>
    /// Issues a long-lived opaque token. Only a hash of it is stored, so a
    /// database leak does not hand an attacker usable refresh tokens.
    /// </summary>
    RefreshToken IssueRefreshToken(Guid userId);

    /// <summary>Validates a refresh token against its stored hash.</summary>
    Task<RefreshTokenValidation> ValidateRefreshTokenAsync(string token, CancellationToken ct);
}

public readonly record struct AccessToken(string Token, DateTimeOffset ExpiresAt);

public readonly record struct RefreshToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>
/// <paramref name="IsReused"/> means a valid, unexpired token was presented
/// a second time. That is a theft signal, not a client bug: the caller must
/// revoke the user's entire token family, not just reject this request.
/// </summary>
public readonly record struct RefreshTokenValidation(
    bool IsValid,
    Guid UserId,
    bool IsReused)
{
    public static RefreshTokenValidation Invalid { get; } = new(false, Guid.Empty, false);

    public static RefreshTokenValidation Reused(Guid userId) => new(false, userId, true);
}
