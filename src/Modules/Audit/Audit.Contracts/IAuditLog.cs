namespace Healthcare.Modules.Audit.Contracts;

/// <summary>
/// Append-only record of a security-relevant event.
///
/// The spec requires this for prescription actions (§10) and authentication
/// failures (§23). Identity writes here through its own ISecurityEventRecorder,
/// so no module reaches into the audit schema — the same one-directional rule
/// every other boundary follows.
/// </summary>
public sealed record AuditEntry(
    DateTimeOffset OccurredAt,
    string Category,
    string Action,
    Guid? UserId,
    string? Subject,
    string Outcome,
    string? Detail);

public static class AuditCategories
{
    public const string Authentication = "auth";
    public const string Authorization = "authz";
    public const string Prescription = "prescription";
    public const string Inventory = "inventory";
    public const string Payment = "payment";
}

public static class AuditOutcomes
{
    public const string Success = "success";
    public const string Failure = "failure";
}

/// <summary>
/// Read access for administration (§16). Separate from the write interface so
/// a module can record an event without also being able to read the whole log.
/// </summary>
public interface IAuditLogReader
{
    Task<IReadOnlyCollection<AuditEntry>> GetForUserAsync(Guid userId, int take, CancellationToken ct);

    Task<IReadOnlyCollection<AuditEntry>> GetRecentAsync(int take, CancellationToken ct);
}