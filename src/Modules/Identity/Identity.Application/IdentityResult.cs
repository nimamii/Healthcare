namespace Healthcare.Modules.Identity.Application;

/// <summary>
/// Result of an authentication attempt. Deliberately coarse: telling a caller
/// whether the email exists but the password was wrong turns registration
/// into an account enumeration oracle.
/// </summary>
public readonly record struct IdentityResult(bool Succeeded, string? ErrorCode, string? ErrorMessage)
{
    public static IdentityResult Success() => new(true, null, null);

    public static IdentityResult InvalidCredentials() =>
        new(false, "invalid_credentials", "ایمیل یا رمز عبور نادرست است.");

    public static IdentityResult EmailAlreadyRegistered() =>
        new(false, "email_taken", "این ایمیل قبلاً ثبت شده است.");
}
