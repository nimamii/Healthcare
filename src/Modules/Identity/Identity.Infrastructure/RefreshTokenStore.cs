using Healthcare.Modules.Identity.Application;
using Microsoft.EntityFrameworkCore;

namespace Healthcare.Modules.Identity.Infrastructure;

public sealed class RefreshTokenStore(IdentityDbContext db, TimeProvider clock) : IRefreshTokenStore
{
    public async Task StoreAsync(Guid userId, string tokenHash, DateTimeOffset expiresAt, CancellationToken ct)
    {
        await db.RefreshTokens.AddAsync(
            new RefreshTokenEntity
            {
                UserId = userId,
                TokenHash = tokenHash,
                ExpiresAt = expiresAt,
                CreatedAt = clock.GetUtcNow(),
            },
            ct);
    }

    public async Task<RefreshTokenRecord?> FindAsync(string tokenHash, CancellationToken ct)
    {
        var entity = await db.RefreshTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

        return entity is null
            ? null
            : new RefreshTokenRecord(entity.UserId, entity.ExpiresAt, entity.RevokedAt);
    }

    public async Task RevokeAsync(string tokenHash, CancellationToken ct)
    {
        var entity = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

        if (entity is not null && entity.RevokedAt is null)
        {
            entity.RevokedAt = clock.GetUtcNow();
        }
    }

    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();

        await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
    }
}
