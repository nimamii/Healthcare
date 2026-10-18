namespace Healthcare.SharedKernel;

/// <summary>
/// Records a security-relevant event for later inspection.
/// The spec requires tracking authentication failures (§23) and prescription
/// actions (§10).
///
/// This lives in the shared kernel rather than in either module because both
/// sides need it and neither should depend on the other: Identity produces
/// events and has no business knowing how they are stored, while Audit stores
/// them and has no business knowing who produces them. The composition root
/// joins them.
///
/// A more specific interface per module would be the alternative, but the
/// shape of the event is identical across producers — who, what, when, did it
/// succeed — so one shared contract is simpler than N near-identical ones.
/// </summary>
public interface ISecurityEventRecorder
{
    Task RecordAsync(SecurityEvent securityEvent, CancellationToken ct);
}

/// <summary>
/// <paramref name="Email"/> is the subject, not necessarily the actor: a failed
/// login records the address that was attempted, which may belong to nobody.
/// <paramref name="Detail"/> must never carry secrets — no password, no token,
/// not even a hash.
/// </summary>
public sealed record SecurityEvent(
    string EventType,
    Guid? UserId,
    string? Email,
    string? IpAddress,
    bool Succeeded,
    DateTimeOffset OccurredAt,
    string? Detail);