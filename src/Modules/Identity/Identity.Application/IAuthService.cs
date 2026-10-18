namespace Healthcare.Modules.Identity.Application;

public interface IAuthService
{
    Task<IdentityResult> RegisterAsync(RegisterRequest request, CancellationToken ct);

    Task<AuthenticationOutcome> AuthenticateAsync(LoginRequest request, CancellationToken ct);

    Task<AuthenticationOutcome> RefreshAsync(string refreshToken, CancellationToken ct);

    Task LogoutAsync(Guid userId, CancellationToken ct);

    Task<IdentityResult> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct);
}

public sealed record RegisterRequest(string Email, string Password);

public sealed record LoginRequest(string Email, string Password);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>
/// A successful authentication. Returned only after the password has been
/// verified server-side — never on the strength of a client claim.
/// </summary>
public readonly record struct AuthenticationOutcome(
    bool Succeeded,
    string? AccessToken,
    string? RefreshToken,
    DateTimeOffset? ExpiresAt,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static AuthenticationOutcome Success(string access, string refresh, DateTimeOffset expiresAt) =>
        new(true, access, refresh, expiresAt, null, null);

    public static AuthenticationOutcome Failed(string errorCode, string errorMessage) =>
        new(false, null, null, null, errorCode, errorMessage);
}
