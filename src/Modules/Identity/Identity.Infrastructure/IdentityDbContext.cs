using Healthcare.Modules.Identity.Domain;
using Healthcare.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Healthcare.Modules.Identity.Infrastructure;

/// <summary>
/// Identity owns this context and this schema. No other module may reference
/// it — cross-module reads go through IUserDirectory instead.
/// </summary>
public class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public const string Schema = "identity";

    public DbSet<User> Users => Set<User>();

    public DbSet<UserRole> UserRoles => Set<UserRole>();

    public DbSet<RefreshTokenEntity> RefreshTokens => Set<RefreshTokenEntity>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema(Schema);

        builder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(u => u.Id);

            entity.Property(u => u.Email).HasMaxLength(320).IsRequired();
            entity.Property(u => u.PasswordHash).HasMaxLength(512).IsRequired();

            // Emails are normalised to lowercase on write, so a plain unique
            // index is enough. A functional index would only be needed if we
            // ever stopped normalising.
            entity.HasIndex(u => u.Email).IsUnique();

            entity.HasMany(u => u.Roles)
                  .WithOne()
                  .HasForeignKey(r => r.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<UserRole>(entity =>
        {
            entity.ToTable("user_roles");

            // Composite key: a user cannot hold the same role twice.
            entity.HasKey(r => new { r.UserId, r.Role });
            entity.Property(r => r.Role).HasMaxLength(64).IsRequired();
        });

        builder.Entity<RefreshTokenEntity>(entity =>
        {
            entity.ToTable("refresh_tokens");
            entity.HasKey(t => t.Id);

            // Only the hash is stored. A database dump must not yield
            // usable refresh tokens.
            entity.Property(t => t.TokenHash).HasMaxLength(128).IsRequired();
            entity.HasIndex(t => t.TokenHash).IsUnique();
            entity.HasIndex(t => t.UserId);
            entity.HasIndex(t => t.ExpiresAt);
        });

        // snake_case columns, per docs/CONVENTIONS.md. Applied across the whole
        // model so a new entity cannot quietly opt out.
        //
        // Must run AFTER the configurations above. HasOne<T>() derives a column
        // name from the relationship and overwrites whatever was set here; when
        // a table is the target of more than one relationship that derived name
        // is suffixed, producing a name no column matches. This pass is
        // therefore the last word, not the first.
        foreach (var entity in builder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }
        }
    }

    /// <summary>
    /// snake_case columns, per docs/CONVENTIONS.md.
    ///
    /// EF Core emits PascalCase by default, which contradicts the documented
    /// convention and means every hand-written query needs quoted identifiers.
    /// Applied across the whole model rather than per-property so a new entity
    /// cannot quietly opt out.
    ///
    /// Must run AFTER the entity configurations above. HasOne&lt;T&gt;() derives
    /// a column name from the relationship and overwrites whatever was set
    /// here; when a table is the target of more than one relationship that
    /// derived name is suffixed, producing "ProductId1" — which no column
    /// matches. This pass is therefore the last word, not the first.
    /// </summary>
    private static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name) || !name.Any(char.IsUpper))
        {
            return name;
        }

        var builder = new System.Text.StringBuilder(name.Length + 4);

        for (var i = 0; i < name.Length; i++)
        {
            var current = name[i];

            // Separator before an uppercase letter, unless it is the first
            // character or follows one. "PasswordHash" -> "password_hash";
            // "OTelTraceId" -> "o_tel_trace_id" is acceptable and stable.
            if (i > 0 && char.IsUpper(current) && name[i - 1] != '_')
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(current));
        }

        return builder.ToString();
    }
}

/// <summary>
/// Exposes this module's context to the application layer through the shared
/// contract. The application code depends on <see cref="IUnitOfWork"/>, not on
/// EF Core, so it stays testable and free of an ORM reference.
/// </summary>
public sealed class IdentityUnitOfWork(IdentityDbContext db) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}