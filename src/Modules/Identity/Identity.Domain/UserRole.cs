namespace Healthcare.Modules.Identity.Domain;

/// <summary>
/// Role assignment. Kept as its own entity (rather than a string column on
/// User) so that role history stays auditable and a single user can hold
/// several roles.
/// </summary>
public class UserRole
{
    private UserRole()
    {
        // EF Core.
    }

    internal UserRole(Guid userId, string role)
    {
        UserId = userId;
        Role = role;
        GrantedAt = DateTimeOffset.UtcNow;
    }

    public Guid UserId { get; private set; }

    public string Role { get; private set; } = string.Empty;

    public DateTimeOffset GrantedAt { get; private set; }
}
