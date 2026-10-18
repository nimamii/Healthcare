using Healthcare.SharedKernel;

namespace Healthcare.Modules.Geography.Domain;

/// <summary>
/// A geographic area a supplier delivers to within normal terms.
/// </summary>
/// <remarks>
/// A supplier may define several. A medical distributor plausibly covers one
/// city plus a corridor out of it, and forcing that into a single polygon would
/// mean drawing a shape that includes places they do not serve.
///
/// Ownership: <see cref="SupplierId"/> is a plain id with no foreign key. The
/// Suppliers module owns that reference and lives in another schema, and the
/// spec forbids the cross-module database coupling an FK would create (§13).
/// </remarks>
public sealed class ServiceArea
{
    private ServiceArea()
    {
        // EF Core.
    }

    private ServiceArea(
        Guid id,
        Guid supplierId,
        string name,
        string polygonWkt,
        decimal? outOfAreaSurcharge,
        string? outOfAreaCurrency,
        DateTimeOffset now)
    {
        Id = id;
        SupplierId = supplierId;
        Name = name;
        PolygonWkt = polygonWkt;
        OutOfAreaSurcharge = outOfAreaSurcharge;
        OutOfAreaCurrency = outOfAreaCurrency;
        IsActive = true;
        CreatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>SRID 4326 (WGS84), the storage standard for this column.</summary>
    public const int Srid = 4326;

    public Guid Id { get; private set; }

    public Guid SupplierId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Polygon in Well-Known Text.
    ///
    /// Held as text rather than as a spatial type so the domain stays free of
    /// an Npgsql dependency and the rules can be unit-tested without a
    /// database. The Infrastructure layer is what converts this to geometry.
    /// </summary>
    public string PolygonWkt { get; private set; } = string.Empty;

    /// <summary>
    /// Extra charge for delivering outside this area.
    ///
    /// Null rather than zero when there is no surcharge, so "not configured"
    /// cannot be read as "free".
    /// </summary>
    public decimal? OutOfAreaSurcharge { get; private set; }

    public string? OutOfAreaCurrency { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Result<ServiceArea> Create(
        Guid supplierId,
        string name,
        string polygonWkt,
        decimal? outOfAreaSurcharge,
        string? outOfAreaCurrency,
        DateTimeOffset now)
    {
        if (supplierId == Guid.Empty)
        {
            return Result<ServiceArea>.Failure(
                "service_area_supplier_required",
                "تأمین‌کننده الزامی است.");
        }

        var trimmedName = name?.Trim() ?? string.Empty;

        if (trimmedName.Length == 0)
        {
            return Result<ServiceArea>.Failure(
                "service_area_name_required",
                "نام محدوده خدمات الزامی است.");
        }

        if (trimmedName.Length > 200)
        {
            return Result<ServiceArea>.Failure(
                "service_area_name_too_long",
                "نام محدوده خدمات نباید بیش از ۲۰۰ کاراکتر باشد.");
        }

        var wktValidation = ValidatePolygonWkt(polygonWkt);

        if (!wktValidation.IsSuccess)
        {
            return Result<ServiceArea>.Failure(wktValidation.ErrorCode!, wktValidation.ErrorMessage!);
        }

        if (outOfAreaSurcharge is < 0)
        {
            return Result<ServiceArea>.Failure(
                "service_area_surcharge_negative",
                "هزینه اضافه نمی‌تواند منفی باشد.");
        }

        // A surcharge with no currency is unusable at checkout: the customer
        // would be shown a number with nothing to attach it to. Rejecting it at
        // write time beats a confusing order later.
        var normalizedCurrency = string.IsNullOrWhiteSpace(outOfAreaCurrency)
            ? null
            : outOfAreaCurrency.Trim().ToUpperInvariant();

        if (outOfAreaSurcharge is > 0 && normalizedCurrency is null)
        {
            return Result<ServiceArea>.Failure(
                "service_area_currency_required",
                "برای هزینه اضافه، واحد پول الزامی است.");
        }

        if (normalizedCurrency is { Length: not 3 })
        {
            return Result<ServiceArea>.Failure(
                "service_area_currency_invalid",
                "کد واحد پول باید سه حرف باشد (مثل IRR یا USD).");
        }

        if (normalizedCurrency is not null
            && normalizedCurrency.Any(c => !char.IsAsciiLetter(c)))
        {
            return Result<ServiceArea>.Failure(
                "service_area_currency_invalid",
                "کد واحد پول باید فقط شامل حروف انگلیسی باشد.");
        }

        return Result<ServiceArea>.Success(new ServiceArea(
            Guid.NewGuid(),
            supplierId,
            trimmedName,
            polygonWkt.Trim(),
            outOfAreaSurcharge,
            normalizedCurrency,
            now));
    }

    /// <summary>
    /// Shallow WKT validation.
    ///
    /// Deliberately not a full geometry check: a self-intersecting polygon is
    /// only detectable by PostGIS, and rejecting on structure here would mean
    /// two sources of truth. This catches the malformed input a caller can
    /// actually get wrong — a truncated string, or a LINESTRING sent instead of
    /// a polygon — and leaves validity to the database, which is authoritative.
    /// </summary>
    private static Result ValidatePolygonWkt(string? wkt)
    {
        if (string.IsNullOrWhiteSpace(wkt))
        {
            return Result.Failure(
                "service_area_polygon_required",
                "چندضلعی محدوده خدمات الزامی است.");
        }

        var trimmed = wkt.Trim();

        // PostGIS accepts both cases; requiring one spelling would reject
        // valid input for no benefit.
        var upper = trimmed.ToUpperInvariant();

        const string polygonPrefix = "POLYGON";
        const string multiPolygonPrefix = "MULTIPOLYGON";

        if (!upper.StartsWith(polygonPrefix, StringComparison.Ordinal)
            && !upper.StartsWith(multiPolygonPrefix, StringComparison.Ordinal))
        {
            return Result.Failure(
                "service_area_polygon_invalid",
                "چندضلعی باید با POLYGON یا MULTIPOLYGON شروع شود.");
        }

        // A polygon is "((" ... "))" for MULTIPOLYGON or "(" ... ")" for
        // POLYGON, and must be closed with the same ring count it opened.
        var opened = trimmed.Count(c => c == '(');
        var closed = trimmed.Count(c => c == ')');

        if (opened == 0 || opened != closed)
        {
            return Result.Failure(
                "service_area_polygon_unbalanced",
                "ساختار چندضلعی ناقص است؛ پرانتزها باید جفت باشند.");
        }

        if (!trimmed.Contains(','))
        {
            return Result.Failure(
                "service_area_polygon_no_vertices",
                "چندضلعی باید حداقل سه نقطه داشته باشد.");
        }

        return Result.Success();
    }

    public void UpdateDetails(
        string name,
        string polygonWkt,
        decimal? outOfAreaSurcharge,
        string? outOfAreaCurrency,
        DateTimeOffset now)
    {
        Name = name.Trim();
        PolygonWkt = polygonWkt.Trim();
        OutOfAreaSurcharge = outOfAreaSurcharge;
        OutOfAreaCurrency = outOfAreaCurrency?.Trim().ToUpperInvariant();
        UpdatedAt = now;
    }

    public void Deactivate(DateTimeOffset now)
    {
        IsActive = false;
        UpdatedAt = now;
    }

    public void Reactivate(DateTimeOffset now)
    {
        IsActive = true;
        UpdatedAt = now;
    }
}