using System.ComponentModel.DataAnnotations;

namespace Healthcare.Modules.Identity.Infrastructure;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; init; } = string.Empty;

    [Required]
    public string Audience { get; init; } = string.Empty;

    /// <summary>
    /// Signing key. Must come from configuration or a secret store — never a
    /// literal in code or a committed file (§24).
    /// </summary>
    [Required]
    [MinLength(32)]
    public string SigningKey { get; init; } = string.Empty;

    [Range(5, 1440)]
    public int AccessTokenMinutes { get; init; } = 15;

    [Range(1, 90)]
    public int RefreshTokenDays { get; init; } = 14;

    /// <summary>
    /// Tolerance for clock drift between the issuing and validating hosts.
    /// Without it, a token issued seconds before a skew can look prematurely
    /// expired.
    /// </summary>
    [Range(0, 300)]
    public int ClockSkewSeconds { get; init; } = 30;
}
