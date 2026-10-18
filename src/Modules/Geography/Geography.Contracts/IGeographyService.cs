namespace Healthcare.Modules.Geography.Contracts;

/// <summary>
/// How other modules ask whether a supplier can serve an address.
/// </summary>
/// <remarks>
/// Read-only on purpose. Orders and Fulfillment need to evaluate
/// eligibility constantly; changing a boundary is an administrative action and
/// belongs to a separate interface so a module cannot widen its own
/// coverage.
/// </remarks>
public interface IGeographyReader
{
    /// <summary>
    /// Whether <paramref name="supplierId"/> serves <paramref name="location"/>,
    /// and what extra delivery would cost if it does not.
    ///
    /// Never returns null: a supplier with no defined areas is
    /// <see cref="ServiceEligibility.Unrestricted"/> rather than unservable.
    /// The distinction matters — treating "no areas yet" as "cannot serve"
    /// would make every newly onboarded supplier invisible (§1).
    /// </summary>
    Task<ServiceEligibility> EvaluateAsync(
        Guid supplierId,
        GeoPoint location,
        CancellationToken ct);

    /// <summary>All active areas a supplier has defined.</summary>
    Task<IReadOnlyCollection<ServiceAreaSummary>> GetAreasAsync(
        Guid supplierId,
        CancellationToken ct);
}

/// <summary>A WGS84 coordinate.</summary>
/// <param name="Longitude">-180 to 180.</param>
/// <param name="Latitude">-90 to 90.</param>
public readonly record struct GeoPoint(double Latitude, double Longitude);

public readonly record struct ServiceAreaSummary(
    Guid Id,
    Guid SupplierId,
    string Name,
    decimal? OutOfAreaSurcharge,
    string? OutOfAreaCurrency,
    bool IsActive);

/// <summary>
/// The eligibility answer, duplicated here so the host does not have to
/// reference the Geography domain assembly.
///
/// Same shape as <c>ServiceEligibility</c> in the domain, and the
/// Infrastructure layer maps one to the other.
/// </summary>
public readonly record struct ServiceEligibility(
    bool IsInArea,
    Guid? ServiceAreaId,
    decimal? Surcharge,
    string? Currency,
    bool RequiresCustomerConfirmation)
{
    /// <summary>No areas defined, so coverage is unrestricted.</summary>
    public static ServiceEligibility Unrestricted() =>
        new(true, null, null, null, false);

    public static ServiceEligibility InArea(Guid serviceAreaId) =>
        new(true, serviceAreaId, null, null, false);

    public static ServiceEligibility OutsideAreaWithSurcharge(decimal surcharge, string currency) =>
        new(false, null, surcharge, currency, true);

    public static ServiceEligibility OutsideAreaWithoutSurcharge() =>
        new(false, null, null, null, false);
}