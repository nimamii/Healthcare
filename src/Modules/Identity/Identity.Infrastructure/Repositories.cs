using Healthcare.Modules.Identity.Application;
using Healthcare.Modules.Identity.Contracts;
using Healthcare.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace Healthcare.Modules.Identity.Infrastructure;

public sealed class UserRepository(IdentityDbContext db) : IUserRepository
{
    public Task<User?> FindByEmailAsync(string email, CancellationToken ct) =>
        db.Users.Include(u => u.Roles)
               .FirstOrDefaultAsync(u => u.Email == email.ToLowerInvariant(), ct);

    public Task<User?> FindByIdAsync(Guid id, CancellationToken ct) =>
        db.Users.Include(u => u.Roles)
               .FirstOrDefaultAsync(u => u.Id == id, ct);

    public async Task AddAsync(User user, CancellationToken ct) => await db.Users.AddAsync(user, ct);

    public void Update(User user) => db.Users.Update(user);

    public Task<bool> EmailExistsAsync(string email, CancellationToken ct) =>
        db.Users.AnyAsync(u => u.Email == email.ToLowerInvariant(), ct);
}

public sealed class UserDirectory(IdentityDbContext db) : IUserDirectory
{
    public async Task<UserSummary?> FindAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users
            .AsNoTracking()
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null)
        {
            return null;
        }

        return new UserSummary(
            user.Id,
            user.Email,
            user.IsActive,
            user.Roles.Select(r => r.Role).ToArray());
    }

    public async Task<bool> HasRoleAsync(Guid userId, string role, CancellationToken ct) =>
        await db.UserRoles
            .AsNoTracking()
            .AnyAsync(r => r.UserId == userId && r.Role == role, ct);

    public async Task<IReadOnlyCollection<string>> GetRolesAsync(Guid userId, CancellationToken ct) =>
        await db.UserRoles
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .Select(r => r.Role)
            .ToArrayAsync(ct);
}
