using Healthcare.Modules.Geography.Contracts;
using Healthcare.Modules.Geography.Domain;
using Healthcare.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Healthcare.Modules.Geography.Infrastructure;

/// <summary>
/// Point-in-polygon evaluation against PostGIS.
/// </summary>
/// <remarks>
/// The decision this method makes is the one the spec is most specific about
/// (§1): a customer outside a supplier's area is not blocked. Every branch
/// below returns something deliverable. The only "no" is a surcharge, and a
/// surcharge is a question to the customer, not a refusal.
/// </remarks>
public sealed class GeographyService(
    GeographyDbContext db,
    string connectionString,
    ILogger<GeographyService> logger) : IGeographyReader
{
    /// <summary>
    /// Areas are evaluated in the database rather than in memory, so a supplier
    /// with a detailed boundary does not have it loaded into the application on
    /// every checkout. ST_Contains is backed by a GIST index on the polygon
    /// column.
    /// </summary>
    public async Task<ServiceEligibility> EvaluateAsync(
        Guid supplierId,
        GeoPoint location,
        CancellationToken ct)
    {
        // A supplier with no areas is treated as serving everywhere. That is
        // the opposite of "serving nowhere", and getting it backwards would
        // make every newly onboarded supplier invisible.
        var areas = await db.ServiceAreas
            .AsNoTracking()
            .Where(a => a.SupplierId == supplierId && a.IsActive)
            .Select(a => new
            {
                a.Id,
                a.PolygonWkt,
                a.OutOfAreaSurcharge,
                a.OutOfAreaCurrency,
            })
            .ToListAsync(ct);

        if (areas.Count == 0)
        {
            return ServiceEligibility.Unrestricted();
        }

        // The containment test runs in PostGIS rather than in C#: loading every
        // boundary to compare in memory would be O(n) per checkout and would
        // reimplement maths that is already correct and indexed.
        //
        // Issued through ADO.NET rather than EF's scalar SqlQuery, whose
        // generated form wraps the caller's SQL and produced a query referencing
        // a "t.Value" column that does not exist. Raw ADO.NET executes exactly
        // what is written below, which is the property that matters here.
        var matchingAreaId = await FindContainingAreaAsync(supplierId, location, ct);

        if (matchingAreaId is { } areaId)
        {
            return ServiceEligibility.InArea(areaId);
        }

        // Outside every area. If the supplier configured a surcharge, the
        // customer is asked to accept it; if not, delivery proceeds anyway.
        var surcharge = areas
            .Select(a => a.OutOfAreaSurcharge)
            .Where(s => s is > 0)
            .Max();

        if (surcharge is null or 0)
        {
            return ServiceEligibility.OutsideAreaWithoutSurcharge();
        }

        var currency = areas
            .FirstOrDefault(a => a.OutOfAreaSurcharge == surcharge)?
            .OutOfAreaCurrency;

        if (surcharge is { } amount && !string.IsNullOrWhiteSpace(currency))
        {
            logger.LogInformation(
                "Address {Latitude},{Longitude} is outside every area for supplier {SupplierId}; "
                + "surcharge {Surcharge} {Currency} requires customer confirmation",
                location.Latitude,
                location.Longitude,
                supplierId,
                amount,
                currency);

            return ServiceEligibility.OutsideAreaWithSurcharge(amount, currency);
        }

        // A surcharge without a currency cannot be shown to a customer, so it
        // is not applied. The domain rejects that combination on write; this
        // branch covers data that predates the rule.
        logger.LogWarning(
            "Supplier {SupplierId} has a surcharge with no currency at {Latitude},{Longitude}; "
            + "treating as no surcharge",
            supplierId,
            location.Latitude,
            location.Longitude);

        return ServiceEligibility.OutsideAreaWithoutSurcharge();
    }

    public async Task<IReadOnlyCollection<ServiceAreaSummary>> GetAreasAsync(
        Guid supplierId,
        CancellationToken ct)
    {
        var areas = await db.ServiceAreas
            .AsNoTracking()
            .Where(a => a.SupplierId == supplierId)
            .OrderBy(a => a.Name)
            .Select(a => new ServiceAreaSummary(
                a.Id,
                a.SupplierId,
                a.Name,
                a.OutOfAreaSurcharge,
                a.OutOfAreaCurrency,
                a.IsActive))
            .ToListAsync(ct);

        return areas;
    }

    /// <summary>
    /// Finds the area containing the given point, or null if none does.
    /// </summary>
    /// <remarks>
    /// Uses raw ADO.NET rather than EF's scalar query, which wraps the SQL
    /// and produces a "t.Value" column reference that does not exist in the
    /// database. The query is identical to the one in the integration tests.
    /// </remarks>
    private async Task<Guid?> FindContainingAreaAsync(
        Guid supplierId,
        GeoPoint location,
        CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);

        await using var command = new NpgsqlCommand(
            """
            SELECT id
            FROM geography.service_areas
            WHERE supplier_id = @supplier
              AND is_active
              AND ST_Contains(
                    polygon,
                    ST_SetSRID(
                        ST_MakePoint(@lon, @lat),
                        4326))
            LIMIT 1
            """,
            connection);

        command.Parameters.AddWithValue("supplier", supplierId);
        command.Parameters.AddWithValue("lon", location.Longitude);
        command.Parameters.AddWithValue("lat", location.Latitude);

        var result = await command.ExecuteScalarAsync(ct);
        return result is Guid id ? id : null;
    }
}