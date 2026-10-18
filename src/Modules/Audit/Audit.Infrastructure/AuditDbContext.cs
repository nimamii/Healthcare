using Healthcare.Modules.Audit.Contracts;
using Healthcare.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Healthcare.Modules.Audit.Infrastructure;

/// <summary>
/// One append-only table. An audit log that can be updated is not an audit
/// log, so there is deliberately no update or delete path anywhere in this
/// module.
/// </summary>
public sealed class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
{
    public const string Schema = "audit";

    public DbSet<AuditEntryEntity> Entries => Set<AuditEntryEntity>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema(Schema);

        foreach (var entity in builder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }
        }

        builder.Entity<AuditEntryEntity>(entity =>
        {
            entity.ToTable("entries");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Category).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Action).HasMaxLength(128).IsRequired();
            entity.Property(e => e.Outcome).HasMaxLength(32).IsRequired();
            entity.Property(e => e.Subject).HasMaxLength(320);
            entity.Property(e => e.Detail).HasMaxLength(1024);

            // The two queries this module actually runs: "what happened to
            // this user" and "show me the latest activity".
            entity.HasIndex(e => new { e.UserId, e.OccurredAt });
            entity.HasIndex(e => e.OccurredAt);
        });
    }

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

            if (i > 0 && char.IsUpper(current) && name[i - 1] != '_')
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(current));
        }

        return builder.ToString();
    }
}

public sealed class AuditEntryEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTimeOffset OccurredAt { get; set; }

    public string Category { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public Guid? UserId { get; set; }

    public string? Subject { get; set; }

    public string Outcome { get; set; } = string.Empty;

    public string? Detail { get; set; }
}

public sealed class AuditUnitOfWork(AuditDbContext db) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}