using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

using Healthcare.Modules.Identity.Application;
using Healthcare.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Healthcare.Modules.Identity.Infrastructure;

public sealed class JwtTokenService(
    IOptions<JwtOptions> options,
    IRefreshTokenStore store,
    [FromKeyedServices(UnitOfWorkKeys.Identity)] IUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<JwtTokenService> logger) : ITokenService
{
    private readonly JwtOptions _options = options.Value;

    public AccessToken IssueAccessToken(Guid userId, string email, IEnumerable<string> roles)
    {
        var now = clock.GetUtcNow();
        var expiresAt = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Email, email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(
                JwtRegisteredClaimNames.Iat,
                now.ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64),
        };

        // One claim per role, not a delimited string: role checks in
        // [Authorize(Roles = ...)] and User.IsInRole both work unchanged.
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: SigningCredentials());

        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    public RefreshToken IssueRefreshToken(Guid userId)
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var expiresAt = clock.GetUtcNow().AddDays(_options.RefreshTokenDays);

        return new RefreshToken(raw, expiresAt);
    }

    public async Task<RefreshTokenValidation> ValidateRefreshTokenAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return RefreshTokenValidation.Invalid;
        }

        // AuthService lives in the parent namespace, so it resolves without a
        // qualifier. The old "Services.AuthService" form was left over from
        // when the type was nested one level deeper.
        var hash = AuthService.HashToken(token);

        // RefreshTokenRecord is a record struct, so the nullable return
        // widens to RefreshTokenRecord?. Testing `record is null` and then
        // returning leaves the compiler holding the nullable type, and every
        // member access below fails. The property pattern unwraps it in one
        // step and narrows correctly.
        if (await store.FindAsync(hash, ct) is not { } record)
        {
            return RefreshTokenValidation.Invalid;
        }

        if (record.ExpiresAt <= clock.GetUtcNow())
        {
            return RefreshTokenValidation.Invalid;
        }

        if (record.RevokedAt is not null)
        {
            // Revoked but still valid and unexpired. Rotated legitimately by
            // a client that then retried, or replayed by someone else. We
            // cannot distinguish, so treat as compromise and rotate the
            // whole family.
            logger.LogWarning(
                "Revoked refresh token presented for user {UserId}",
                record.UserId);

            return RefreshTokenValidation.Reused(record.UserId);
        }

        // Rotation: the used token is revoked as part of issuing its
        // replacement, so a second presentation of the same token is
        // detectable above.
        //
        // Committed here because this is the whole point of rotation: without
        // the write, the token stays valid and no replay is ever detected.
        await store.RevokeAsync(hash, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return new RefreshTokenValidation(true, record.UserId, false);
    }

    private SigningCredentials SigningCredentials()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));

        return new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    }
}
