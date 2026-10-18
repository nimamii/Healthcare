using Healthcare.Modules.Audit.Contracts;
using Healthcare.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Healthcare.Modules.Audit.Infrastructure;

public sealed class AuditModule : IModule
{
    public string Name => "audit";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Default is required. Set it via environment variable (§24).");

        services.AddDbContext<AuditDbContext>(options =>
            options.UseNpgsql(connectionString));

        // Keyed, not plain. Every module owns a DbContext, so a single
        // IUnitOfWork registration would be overwritten by whichever module
        // registers last — and every write would then commit the wrong
        // context. See IUnitOfWork for what that looked like in practice.
        services.AddScoped<AuditUnitOfWork>();
        services.AddKeyedScoped<IUnitOfWork>(UnitOfWorkKeys.Audit, (sp, _) =>
            sp.GetRequiredService<AuditUnitOfWork>());
        services.AddScoped<IAuditLogReader, AuditLogReader>();

        // Identity declares ISecurityEventRecorder because it must not depend
        // on this module. Registering the implementation here is what wires
        // the two together, and it is the only place they meet.
        services.AddScoped<ISecurityEventRecorder, SecurityEventRecorder>();
    }

    /// <summary>
/// Does nothing on purpose, for the same reason as
/// <c>IdentityModule.Configure</c>: the schema comes from
/// db/init/001-schemas.sql, which Postgres runs when the volume is first
/// created.
///
/// EnsureCreated was never capable of supporting two modules — it creates
/// the database and its own tables, and does nothing at all when the database
/// already exists — so whichever module ran second silently created nothing.
/// </summary>
public void Configure(IServiceProvider services, IHostEnvironment environment)
    {
    }
}

/// <summary>
/// Translates Identity's security events into audit entries.
///
/// The mapping from event type to category lives here rather than in Identity
/// so that adding an event type does not require the producing module to know
/// anything about audit's vocabulary.
/// </summary>
public sealed class SecurityEventRecorder(
    AuditDbContext db,
    [FromKeyedServices(UnitOfWorkKeys.Audit)] IUnitOfWork unitOfWork,
    ILogger<SecurityEventRecorder> logger) : ISecurityEventRecorder
{
    /// <summary>
    /// Failures are swallowed, deliberately. A failing audit write must not
    /// turn a successful login into a 500 — the caller is an auth handler,
    /// and rejecting a valid credential because the log was unavailable is a
    /// worse outcome than a gap in the log.
    ///
    /// That choice is only defensible if the gap is visible, so the failure is
    /// logged at Error. An alerting rule on this message is what makes the
    /// trade-off safe; without it, events vanish unnoticed.
    /// </summary>
    public async Task RecordAsync(SecurityEvent securityEvent, CancellationToken ct)
    {
        try
        {
            await WriteAsync(securityEvent, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The caller hung up. Not an audit failure.
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to record security event {EventType} for user {UserId}",
                securityEvent.EventType,
                securityEvent.UserId);
        }
    }

    private async Task WriteAsync(SecurityEvent securityEvent, CancellationToken ct)
    {
        db.Entries.Add(new AuditEntryEntity
        {
            OccurredAt = securityEvent.OccurredAt,
            Category = AuditCategories.Authentication,
            Action = securityEvent.EventType,
            UserId = securityEvent.UserId,
            Subject = securityEvent.Email,
            Outcome = securityEvent.Succeeded
                ? AuditOutcomes.Success
                : AuditOutcomes.Failure,
            Detail = securityEvent.Detail,
        });

        await unitOfWork.SaveChangesAsync(ct);
    }
}

public sealed class AuditLogReader(AuditDbContext db) : IAuditLogReader
{
    public async Task<IReadOnlyCollection<AuditEntry>> GetForUserAsync(
        Guid userId,
        int take,
        CancellationToken ct)
    {
        var rows = await db.Entries
            .AsNoTracking()
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.OccurredAt)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync(ct);

        return rows.Select(ToEntry).ToArray();
    }

    public async Task<IReadOnlyCollection<AuditEntry>> GetRecentAsync(int take, CancellationToken ct)
    {
        var rows = await db.Entries
            .AsNoTracking()
            .OrderByDescending(e => e.OccurredAt)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync(ct);

        return rows.Select(ToEntry).ToArray();
    }

    private static AuditEntry ToEntry(AuditEntryEntity e) => new(
        e.OccurredAt,
        e.Category,
        e.Action,
        e.UserId,
        e.Subject,
        e.Outcome,
        e.Detail);
}