using Healthcare.Modules.Geography.Contracts;
using Healthcare.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Healthcare.Modules.Geography.Infrastructure;

public sealed class GeographyModule : IModule
{
    public string Name => "geography";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Default is required. Set it via environment variable (§24).");

        services.AddDbContext<GeographyDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddScoped<GeographyUnitOfWork>();

        // Keyed, so it cannot collide with another module's unit of work.
        // An unkeyed registration here would silently commit the wrong
        // context, which is how writes get discarded with no error.
        services.AddKeyedScoped<IUnitOfWork>(UnitOfWorkKeys.Geography, (sp, _) =>
            sp.GetRequiredService<GeographyUnitOfWork>());

        services.AddScoped<IGeographyReader>(sp =>
            new GeographyService(
                sp.GetRequiredService<GeographyDbContext>(),
                connectionString,
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<GeographyService>>()));
    }

    /// <summary>
    /// Does nothing: the schema comes from db/init/001-schemas.sql, which
    /// Postgres runs when the volume is first created. The same reason as every
    /// other module — DbContext.EnsureCreated cannot support more than one
    /// module, since it does nothing once the database exists.
    /// </summary>
    public void Configure(IServiceProvider services, IHostEnvironment environment)
    {
    }
}