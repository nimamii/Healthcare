using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentValidation;
using FluentValidation.Results;
using Healthcare.Modules.Identity.Application;
using Healthcare.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

// This project uses Microsoft.NET.Sdk with an explicit FrameworkReference,
// not the Web SDK, so none of the ASP.NET Core namespaces are implicit.
// Without these the file looks correct but does not compile.

namespace Healthcare.Modules.Identity.Api;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/identity").WithTags("Identity");

        // Anonymous by design — these are how a caller obtains a token in the
        // first place. Everything else in the API is authenticated by default.
        //
        // These three carry the strict per-IP policy because they are the only
        // unauthenticated surface, and therefore the only brute-force target.
        // The name lives here rather than in the host so the module owns which
        // endpoints are sensitive; the host owns how a limit is enforced.
        group.MapPost("/register", RegisterAsync)
             .AllowAnonymous()
             .RequireRateLimiting(RateLimitPolicies.Authentication)
             .WithName("Register")
             .WithSummary("ثبت‌نام کاربر جدید");

        group.MapPost("/login", LoginAsync)
             .AllowAnonymous()
             .RequireRateLimiting(RateLimitPolicies.Authentication)
             .WithName("Login")
             .WithSummary("ورود و دریافت توکن");

        group.MapPost("/refresh", RefreshAsync)
             .AllowAnonymous()
             .RequireRateLimiting(RateLimitPolicies.Authentication)
             .WithName("RefreshToken")
             .WithSummary("تازه‌سازی توکن دسترسی");

        group.MapPost("/logout", LogoutAsync)
             .RequireAuthorization()
             .WithName("Logout")
             .WithSummary("خروج و ابطال توکن‌ها");

        group.MapPost("/change-password", ChangePasswordAsync)
             .RequireAuthorization()
             .WithName("ChangePassword")
             .WithSummary("تغییر رمز عبور");
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        IAuthService auth,
        CancellationToken ct)
    {
        var validation = new RegisterRequestValidator().Validate(request);

        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToErrorDictionary());
        }

        var result = await auth.RegisterAsync(request, ct);

        return result.Succeeded
            ? Results.Created()
            : Results.Problem(
                title: "ثبت‌نام ناموفق بود",
                detail: result.ErrorMessage,
                statusCode: 400);
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        IAuthService auth,
        CancellationToken ct)
    {
        var validation = new LoginRequestValidator().Validate(request);

        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToErrorDictionary());
        }

        var outcome = await auth.AuthenticateAsync(request, ct);

        return outcome.Succeeded
            ? Results.Ok(new TokenResponse(
                outcome.AccessToken!,
                outcome.RefreshToken!,
                outcome.ExpiresAt!.Value))
            : Results.Problem(
                title: "ورود ناموفق بود",
                detail: outcome.ErrorMessage,
                statusCode: 401);
    }

    private static async Task<IResult> RefreshAsync(
        RefreshRequest request,
        IAuthService auth,
        CancellationToken ct)
    {
        var outcome = await auth.RefreshAsync(request.RefreshToken, ct);

        return outcome.Succeeded
            ? Results.Ok(new TokenResponse(
                outcome.AccessToken!,
                outcome.RefreshToken!,
                outcome.ExpiresAt!.Value))
            : Results.Problem(
                title: "توکن نامعتبر است",
                detail: outcome.ErrorMessage,
                statusCode: 401);
    }

    private static async Task<IResult> LogoutAsync(
        IAuthService auth,
        HttpContext context,
        CancellationToken ct)
    {
        var userId = context.User.GetUserId();

        if (userId is null)
        {
            return Results.Unauthorized();
        }

        await auth.LogoutAsync(userId.Value, ct);

        return Results.NoContent();
    }

    private static async Task<IResult> ChangePasswordAsync(
        ChangePasswordRequest request,
        IAuthService auth,
        HttpContext context,
        CancellationToken ct)
    {
        var userId = context.User.GetUserId();

        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var result = await auth.ChangePasswordAsync(userId.Value, request, ct);

        return result.Succeeded
            ? Results.NoContent()
            : Results.Problem(
                title: "تغییر رمز ناموفق بود",
                detail: result.ErrorMessage,
                statusCode: 400);
    }
}

public sealed record TokenResponse(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

public sealed record RefreshRequest(string RefreshToken);

/// <summary>
/// FluentValidation 11 removed the instance <c>ToDictionary()</c> helper.
/// Mapping explicitly keeps this project free of the ASP.NET integration
/// package it does not otherwise need.
/// </summary>
internal static class ValidationExtensions
{
    public static Dictionary<string, string[]> ToErrorDictionary(
        this ValidationResult result) =>
        result.Errors
            .GroupBy(failure => failure.PropertyName)
            .ToDictionary(
                group => group.Key,
                group => group.Select(failure => failure.ErrorMessage).ToArray());
}

internal static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Reads the user id from the validated token. The signature is the
    /// framework's guarantee that the token was verified — this is precisely
    /// the "never trust the client" boundary the spec calls for (§15).
    /// </summary>
    public static Guid? GetUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub);

        return Guid.TryParse(value, out var id) ? id : null;
    }
}

internal sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("ایمیل الزامی است.")
            .EmailAddress().WithMessage("ایمیل معتبر نیست.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("رمز عبور الزامی است.")
            .MinimumLength(12).WithMessage("رمز عبور باید حداقل ۱۲ کاراکتر باشد.")
            .Matches("[A-Z]").WithMessage("رمز عبور باید شامل حرف بزرگ انگلیسی باشد.")
            .Matches("[a-z]").WithMessage("رمز عبور باید شامل حرف کوچک انگلیسی باشد.")
            .Matches("[0-9]").WithMessage("رمز عبور باید شامل رقم باشد.");
    }
}

internal sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}
