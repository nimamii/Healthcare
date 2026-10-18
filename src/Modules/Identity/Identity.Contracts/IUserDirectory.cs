namespace Healthcare.Modules.Identity.Contracts;

/// <summary>
/// How other modules ask "who is this user, and may they do this?"
///
/// Authorization is enforced server-side (§15), and this is the only
/// sanctioned way for one module to learn about another's users. No module
/// may read the identity schema directly.
/// </summary>
public interface IUserDirectory
{
    Task<UserSummary?> FindAsync(Guid userId, CancellationToken ct);

    Task<bool> HasRoleAsync(Guid userId, string role, CancellationToken ct);

    Task<IReadOnlyCollection<string>> GetRolesAsync(Guid userId, CancellationToken ct);
}

public readonly record struct UserSummary(
    Guid Id,
    string Email,
    bool IsActive,
    IReadOnlyCollection<string> Roles);
