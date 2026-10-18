namespace Healthcare.Modules.Identity.Domain;

/// <summary>
/// A platform account. Identity and authorization both live here; the profile
/// details (name, address, phone) belong to the Users module and are reached
/// across the module boundary, never by a foreign key into another schema.
/// </summary>
public class User
{
    private readonly List<UserRole> _roles = [];

    private User()
    {
        // EF Core.
    }

    private User(string email, string passwordHash, DateTimeOffset createdAt)
    {
        Id = Guid.NewGuid();
        Email = email;
        PasswordHash = passwordHash;
        CreatedAt = createdAt;
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public string Email { get; private set; } = string.Empty;

    public string PasswordHash { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? DeactivatedAt { get; private set; }

    public DateTimeOffset? LastLoginAt { get; private set; }

    public DateTimeOffset? PasswordChangedAt { get; private set; }

    public IReadOnlyCollection<UserRole> Roles => _roles.AsReadOnly();

    public static User Create(string email, string passwordHash, DateTimeOffset now) =>
        new(email.Trim().ToLowerInvariant(), passwordHash, now);

    public void AddRole(string role)
    {
        if (_roles.Any(r => r.Role == role))
        {
            return;
        }

        _roles.Add(new UserRole(Id, role));
    }

    public void RemoveRole(string role)
    {
        var existing = _roles.FirstOrDefault(r => r.Role == role);

        if (existing is not null)
        {
            _roles.Remove(existing);
        }
    }

    public void RecordLogin(DateTimeOffset now) => LastLoginAt = now;

    /// <summary>
    /// The caller has already verified the current password; this only
    /// stores the new hash. Keeping verification outside the entity means
    /// the domain never sees the plaintext.
    /// </summary>
    public void ChangePassword(string newPasswordHash, DateTimeOffset now)
    {
        PasswordHash = newPasswordHash;
        PasswordChangedAt = now;
    }

    /// <summary>
    /// Deactivation is not deletion. Prescriptions and audit rows reference
    /// users; a hard delete would either break those references or cascade
    /// away history the spec requires us to keep (§10).
    /// </summary>
    public void Deactivate(DateTimeOffset now)
    {
        IsActive = false;
        DeactivatedAt = now;
    }

    public void Reactivate()
    {
        IsActive = true;
        DeactivatedAt = null;
    }
}
