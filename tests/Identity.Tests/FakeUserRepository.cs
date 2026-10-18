using Healthcare.Modules.Identity.Application;
using Healthcare.Modules.Identity.Domain;
using Healthcare.SharedKernel;

namespace Healthcare.Tests.Identity;

internal sealed class FakeUserRepository : IUserRepository
{
    private readonly Dictionary<Guid, User> _users = [];
    private readonly Dictionary<string, Guid> _byEmail = new(StringComparer.OrdinalIgnoreCase);

    public void Seed(User user)
    {
        _users[user.Id] = user;
        _byEmail[user.Email] = user.Id;
    }

    public User? FindByEmail(string email) =>
        _byEmail.TryGetValue(email, out var id) ? _users[id] : null;

    public Task<User?> FindByEmailAsync(string email, CancellationToken ct) =>
        Task.FromResult(FindByEmail(email));

    public Task<User?> FindByIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(_users.GetValueOrDefault(id));

    public Task AddAsync(User user, CancellationToken ct)
    {
        Seed(user);
        return Task.CompletedTask;
    }

    public void Update(User user) => Seed(user);

    public Task<bool> EmailExistsAsync(string email, CancellationToken ct) =>
        Task.FromResult(_byEmail.ContainsKey(email));
}

internal sealed class FakeRefreshTokenStore : IRefreshTokenStore
{
    private readonly Dictionary<string, (Guid UserId, DateTimeOffset ExpiresAt, DateTimeOffset? RevokedAt)> _tokens = [];

    public List<string> RevokeAllCalls { get; } = [];

    public Task StoreAsync(Guid userId, string tokenHash, DateTimeOffset expiresAt, CancellationToken ct)
    {
        _tokens[tokenHash] = (userId, expiresAt, null);
        return Task.CompletedTask;
    }

    public Task<RefreshTokenRecord?> FindAsync(string tokenHash, CancellationToken ct)
    {
        if (!_tokens.TryGetValue(tokenHash, out var entry))
        {
            return Task.FromResult<RefreshTokenRecord?>(null);
        }

        return Task.FromResult<RefreshTokenRecord?>(
            new RefreshTokenRecord(entry.UserId, entry.ExpiresAt, entry.RevokedAt));
    }

    public Task RevokeAsync(string tokenHash, CancellationToken ct)
    {
        if (_tokens.TryGetValue(tokenHash, out var entry))
        {
            _tokens[tokenHash] = (entry.UserId, entry.ExpiresAt, DateTimeOffset.UtcNow);
        }

        return Task.CompletedTask;
    }

    public Task RevokeAllForUserAsync(Guid userId, CancellationToken ct)
    {
        RevokeAllCalls.Add(userId.ToString());

        foreach (var hash in _tokens
            .Where(t => t.Value.UserId == userId)
            .Select(t => t.Key)
            .ToList())
        {
            var entry = _tokens[hash];
            _tokens[hash] = (entry.UserId, entry.ExpiresAt, DateTimeOffset.UtcNow);
        }

        return Task.CompletedTask;
    }
}

internal sealed class RecordingSecurityEventRecorder : ISecurityEventRecorder
{
    public List<SecurityEvent> Events { get; } = [];

    public Task RecordAsync(SecurityEvent securityEvent, CancellationToken ct)
    {
        Events.Add(securityEvent);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Records commits instead of performing them.
///
/// This exists because of a bug only a running system reveals: AddAsync staged
/// the user, nothing called SaveChanges, the API returned 201, and the
/// database stayed empty. The fake makes the commit observable so a test can
/// assert a write path actually persists.
/// </summary>
internal sealed class RecordingUnitOfWork : IUnitOfWork
{
    public int CommitCount { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken ct)
    {
        CommitCount++;
        return Task.FromResult(1);
    }
}
