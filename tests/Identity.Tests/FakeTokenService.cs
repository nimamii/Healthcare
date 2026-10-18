using Healthcare.Modules.Identity.Application;
using Healthcare.SharedKernel;

/// <summary>
/// Predictable tokens so tests can assert on rotation and reuse without
/// depending on real JWT signing.
///
/// It talks to the same store the real service does, which is what makes the
/// reuse test meaningful: validation has to observe the revocation that
/// rotation performs, exactly as it would in production.
/// </summary>
namespace Healthcare.Tests.Identity;

internal sealed class FakeTokenService(
    IRefreshTokenStore store,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ITokenService
{
    // Separate counters, because a login issues an access token *and* a
    // refresh token from the same call. A shared counter made the issued
    // refresh token "…-2", and a test that assumed "…-1" silently got a
    // token that had never been stored.
    private int _accessCounter;

    private int _refreshCounter;

    public AccessToken IssueAccessToken(Guid userId, string email, IEnumerable<string> roles) =>
        new($"access-{userId}-{++_accessCounter}", clock.GetUtcNow().AddMinutes(15));

    public RefreshToken IssueRefreshToken(Guid userId) =>
        new($"refresh-{userId}-{++_refreshCounter}", clock.GetUtcNow().AddDays(14));

    public async Task<RefreshTokenValidation> ValidateRefreshTokenAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return RefreshTokenValidation.Invalid;
        }

        // FindAsync returns a nullable struct, so it is unwrapped explicitly.
        // Without this the compiler keeps the nullable type and every member
        // access below is an error.
        var found = await store.FindAsync(AuthService.HashToken(token), ct);

        if (found is not { } record || record.ExpiresAt <= clock.GetUtcNow())
        {
            return RefreshTokenValidation.Invalid;
        }

        if (record.RevokedAt is not null)
        {
            // Mirrors the real service: revoked-but-unexpired means either a
            // client retry or a replay. Both are treated as compromise.
            return RefreshTokenValidation.Reused(record.UserId);
        }

        // Rotation: the presented token is spent as it is used.
        await store.RevokeAsync(AuthService.HashToken(token), ct);
        await unitOfWork.SaveChangesAsync(ct);

        return new RefreshTokenValidation(true, record.UserId, false);
    }
}
