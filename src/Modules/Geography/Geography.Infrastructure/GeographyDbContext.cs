using Healthcare.Modules.Geography.Domain;
using Healthcare.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Healthcare.Modules.Geography.Infrastructure;

/// <summary>
/// Geography owns this context and the geography schema.
/// </summary>
/// <remarks>
/// Requires PostGIS: the column is <c>geometry(MultiPolygon, 4326)</c> and
/// point-in-polygon runs in the database. Npgsql maps that type only when the
/// extension is installed, which db/init/001-schemas.sql creates before any
/// table — so a missing extension fails at schema time with a clear error,
/// not at first query.
/// </remarks>
public class GeographyDbContext(DbContextOptions<GeographyDbContext> options) : DbContext(options)
{
    public const string Schema = "geography";

    public DbSet<ServiceArea> ServiceAreas => Set<ServiceArea>();

    /// <summary>
    /// Keeps the geometry column in step with the WKT the domain holds.
    /// </summary>
    /// <remarks>
    /// The two cannot both be mapped: the entity carries text, the column holds
    /// geometry, and Npgsql will not convert a string to geometry on its own.
    /// Letting EF write the WKT directly fails on every insert with a type
    /// error, so the column is written here instead.
    ///
    /// New rows are inserted by raw SQL rather than by EF, because the
    /// geometry has to be present in the same statement: the column is NOT
    /// NULL, so an insert that left it empty would be rejected before a
    /// follow-up UPDATE could fill it. The first version of this method did
    /// exactly that and every service area failed to save.
    ///
    /// ST_Multi promotes a POLYGON to a one-part MULTIPOLYGON so both input
    /// shapes fit the column type.
    ///
    /// All three statements share one transaction: otherwise a failure partway
    /// would leave polygon_wkt and polygon disagreeing, and every later
    /// point-in-polygon query would silently answer from the stale one.
    /// </remarks>
    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        // Captured before the save: EF marks changed entities unchanged on
        // success, so a query after it would find nothing to update.
        var addedEntries = ChangeTracker.Entries<ServiceArea>()
            .Where(e => e.State == EntityState.Added)
            .ToList();

        var modified = ChangeTracker.Entries<ServiceArea>()
            .Where(e => e.State == EntityState.Modified)
            .Select(e => e.Entity)
            .ToList();

        await using var transaction = await Database.BeginTransactionAsync(ct);

        // New rows are inserted here rather than by EF because the geometry
        // has to exist in the same statement. The column is NOT NULL, so an
        // EF insert would be rejected before the UPDATE could populate it —
        // the first version of this method did exactly that and every
        // service area failed to save.
        foreach (var entry in addedEntries)
        {
            var area = entry.Entity;

            await Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO geography.service_areas
                    (id, supplier_id, name, polygon_wkt, polygon,
                     out_of_area_surcharge, out_of_area_currency,
                     is_active, created_at, updated_at)
                VALUES
                    ({area.Id}, {area.SupplierId}, {area.Name}, {area.PolygonWkt},
                     ST_Multi(ST_GeomFromText({area.PolygonWkt}, {ServiceArea.Srid})),
                     {area.OutOfAreaSurcharge}, {area.OutOfAreaCurrency},
                     {area.IsActive}, {area.CreatedAt}, {area.UpdatedAt})
                """,
                ct);

            // EF still believes this row is new; without this it issues a
            // second INSERT against a row that already exists.
            entry.State = EntityState.Unchanged;
        }

        // For modified rows EF writes its mapped columns; the geometry, which
        // it does not know about, is refreshed here.
        foreach (var area in modified)
        {
            await Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE geography.service_areas
                SET polygon = ST_Multi(ST_GeomFromText({area.PolygonWkt}, {ServiceArea.Srid}))
                WHERE id = {area.Id}
                """,
                ct);
        }

        // The entity's mapped columns are updated through EF, excluding the
        // rows inserted above (now Unchanged).
        var affected = await base.SaveChangesAsync(ct);

        // One transaction: a failure partway through would otherwise leave
        // polygon_wkt and polygon disagreeing, and every later point-in-polygon
        // query would answer from the wrong one.
        await transaction.CommitAsync(ct);

        return affected + addedEntries.Count;
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema(Schema);

        // snake_case columns, per docs/CONVENTIONS.md. Runs last so it is the
        // final word over anything a relationship set.
        builder.Entity<ServiceArea>(entity =>
        {
            entity.ToTable("service_areas");
            entity.HasKey(s => s.Id);

            entity.Property(s => s.Name).HasMaxLength(200).IsRequired();

            // The entity holds WKT text; the column holds geometry. Mapping the
            // string straight onto the geometry column makes every insert fail
            // with a type error, so the two are kept distinct:
            //
            //   polygon_wkt  — mapped, the domain's own representation
            //   polygon      — the spatial column, written by raw SQL in
            //                  SaveChangesAsync
            //
            // The GIST index belongs on polygon, not on the text.
            entity.Property(s => s.PolygonWkt)
                  .HasColumnName("polygon_wkt")
                  .HasColumnType("text");

            entity.Property(s => s.OutOfAreaSurcharge).HasPrecision(14, 2);
            entity.Property(s => s.OutOfAreaCurrency).HasMaxLength(3);

            entity.HasIndex(s => new { s.SupplierId, s.Name }).IsUnique();
        });

        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }
        }
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

public sealed class GeographyUnitOfWork(GeographyDbContext db) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}