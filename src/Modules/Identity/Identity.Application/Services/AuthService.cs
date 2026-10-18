using System.Security.Cryptography;
using System.Text;

using Healthcare.Modules.Identity.Application;
using Healthcare.Modules.Identity.Domain;
using Healthcare.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Healthcare.Modules.Identity.Application;

public interface IUserRepository
{
    Task<User?> FindByEmailAsync(string email, CancellationToken ct);

    Task<User?> FindByIdAsync(Guid id, CancellationToken ct);

    Task AddAsync(User user, CancellationToken ct);

    void Update(User user);

    Task<bool> EmailExistsAsync(string email, CancellationToken ct);
}

/// <summary>
/// Authentication and token orchestration. Lives in Application because it
/// coordinates domain objects and ports, but owns no persistence detail —
/// that is what the repository and store interfaces are for.
/// </summary>
public sealed class AuthService(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    IRefreshTokenStore refreshTokens,
    ISecurityEventRecorder securityEvents,
    [FromKeyedServices(UnitOfWorkKeys.Identity)] IUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<AuthService> logger) : IAuthService
{
    private static readonly string[] RolesForNewAccount = [Roles.Customer];

    public async Task<IdentityResult> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        if (await users.EmailExistsAsync(email, ct))
        {
            await RecordEventAsync("register.duplicate", null, email, false, ct);

            return IdentityResult.EmailAlreadyRegistered();
        }

        var user = User.Create(email, passwordHasher.Hash(request.Password), clock.GetUtcNow());

        foreach (var role in RolesForNewAccount)
        {
            user.AddRole(role);
        }

        await users.AddAsync(user, ct);

        // AddAsync only stages the row. Without this the request returns 201
        // and nothing is ever written — which is exactly what happened before
        // this call existed.
        await unitOfWork.SaveChangesAsync(ct);

        await RecordEventAsync("register.success", user.Id, email, true, ct);

        logger.LogInformation("User registered: {UserId}", user.Id);

        return IdentityResult.Success();
    }

    public async Task<AuthenticationOutcome> AuthenticateAsync(LoginRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await users.FindByEmailAsync(email, ct);

        // Every failure returns the identical code and message. A distinct
        // "account_inactive" here would be an oracle: an attacker learns that
        // the email IS registered, which is exactly the information needed to
        // build a list worth credential-stuffing. The real reason is recorded
        // in the audit log, where staff can see it and the client cannot.
        if (user is null)
        {
            await RecordEventAsync("login.failed_unknown_email", null, email, false, ct);

            return InvalidCredentials();
        }

        if (!passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            await RecordEventAsync("login.failed_credentials", user.Id, email, false, ct);

            return InvalidCredentials();
        }

        if (!user.IsActive)
        {
            await RecordEventAsync("login.failed_inactive", user.Id, email, false, ct);

            return InvalidCredentials();
        }

        return await IssueTokensAsync(user, ct, recordLogin: true);
    }

    public async Task<AuthenticationOutcome> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var validation = await tokenService.ValidateRefreshTokenAsync(refreshToken, ct);

        if (!validation.IsValid)
        {
            if (validation.IsReused)
            {
                // A refresh token was replayed. Either the client is buggy or
                // the token leaked. We cannot tell which, so we assume the
                // worse case and cut the attacker's access along with it.
                await refreshTokens.RevokeAllForUserAsync(validation.UserId, ct);
                await unitOfWork.SaveChangesAsync(ct);

                await RecordEventAsync("refresh.reuse_detected", validation.UserId, null, false, ct);

                logger.LogWarning(
                    "Refresh token reuse detected for user {UserId}; all tokens revoked",
                    validation.UserId);
            }

            return AuthenticationOutcome.Failed("invalid_refresh_token", "توکن نامعتبر یا منقضی شده است.");
        }

        var user = await users.FindByIdAsync(validation.UserId, ct);

        if (user is null || !user.IsActive)
        {
            // Cut the session rather than just refusing this one call. If the
            // account is gone or disabled, any live token for it is either
            // abandoned or in the hands of someone it should not be.
            await refreshTokens.RevokeAllForUserAsync(validation.UserId, ct);
            await unitOfWork.SaveChangesAsync(ct);

            await RecordEventAsync(
                user is null ? "refresh.failed_unknown_user" : "refresh.failed_inactive",
                validation.UserId,
                null,
                false,
                ct);

            return AuthenticationOutcome.Failed("account_unavailable", "حساب در دسترس نیست.");
        }

        return await IssueTokensAsync(user, ct, recordLogin: false);
    }

    public async Task LogoutAsync(Guid userId, CancellationToken ct)
    {
        await refreshTokens.RevokeAllForUserAsync(userId, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<IdentityResult> ChangePasswordAsync(
        Guid userId,
        ChangePasswordRequest request,
        CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId, ct);

        if (user is null || !user.IsActive)
        {
            return IdentityResult.InvalidCredentials();
        }

        if (!passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
        {
            await RecordEventAsync("password.change_failed", userId, user.Email, false, ct);

            return IdentityResult.InvalidCredentials();
        }

        user.ChangePassword(passwordHasher.Hash(request.NewPassword), clock.GetUtcNow());
        users.Update(user);

        // A password change must invalidate existing sessions, otherwise a
        // stolen refresh token survives the user locking themselves out.
        await refreshTokens.RevokeAllForUserAsync(userId, ct);

        // One commit for both the new hash and the revocations, so a crash
        // between them cannot leave the old password valid with new tokens.
        await unitOfWork.SaveChangesAsync(ct);

        await RecordEventAsync("password.changed", userId, user.Email, true, ct);

        return IdentityResult.Success();
    }

    private async Task<AuthenticationOutcome> IssueTokensAsync(User user, CancellationToken ct, bool recordLogin)
    {
        var roles = user.Roles.Select(r => r.Role).ToArray();
        var access = tokenService.IssueAccessToken(user.Id, user.Email, roles);
        var refresh = tokenService.IssueRefreshToken(user.Id);

        // A refresh is not a new login. Recording it here would overwrite
        // LastLoginAt every time the access token is silently renewed, so
        // the field would stop meaning "when the person last signed in".
        if (recordLogin)
        {
            user.RecordLogin(clock.GetUtcNow());
            users.Update(user);
        }

        await refreshTokens.StoreAsync(
            user.Id,
            HashToken(refresh.Token),
            refresh.ExpiresAt,
            ct);

        // The LastLoginAt update and the new refresh token are committed
        // together: a token row without its login timestamp would let a later
        // silent refresh look like a fresh sign-in.
        await unitOfWork.SaveChangesAsync(ct);

        await RecordEventAsync("login.success", user.Id, user.Email, true, ct);

        return AuthenticationOutcome.Success(access.Token, refresh.Token, access.ExpiresAt);
    }

    private static AuthenticationOutcome InvalidCredentials() =>
        AuthenticationOutcome.Failed(
            "invalid_credentials",
            IdentityResult.InvalidCredentials().ErrorMessage!);

    private async Task RecordEventAsync(
        string eventType,
        Guid? userId,
        string? email,
        bool succeeded,
        CancellationToken ct)
    {
        try
        {
            await securityEvents.RecordAsync(
                new SecurityEvent(eventType, userId, email, null, succeeded, clock.GetUtcNow(), null),
                ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The caller hung up. That is not an audit failure, and logging it
            // as one would bury real recorder outages in noise.
            throw;
        }
        catch (Exception ex)
        {
            // Auditing must never be the reason a login fails. Log loudly,
            // carry on, and let alerting catch the gap.
            logger.LogError(ex, "Failed to record security event {EventType}", eventType);
        }
    }

    /// <summary>
    /// Hashes a refresh token for storage and lookup.
    ///
    /// Public because two other assemblies must agree on this exact scheme:
    /// the JWT service hashes a presented token to find its record, and the
    /// tests assert that the raw token is not what gets stored. A duplicate
    /// implementation here would be a security bug waiting to happen, so
    /// there is one function and it is shared.
    /// </summary>
    public static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
