using Healthcare.Modules.Identity.Application;
using Healthcare.Modules.Identity.Contracts;
using Healthcare.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Healthcare.Modules.Identity.Infrastructure;

public sealed class IdentityModule : IModule
{
    public string Name => "identity";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Default is required. Set it via environment variable — "
                + "do not hard-code it (§24).");

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

        services.AddDbContext<IdentityDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsAssembly(
                    typeof(IdentityModule).Assembly.GetName().Name)));

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IRefreshTokenStore, RefreshTokenStore>();
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<ITokenService, JwtTokenService>();

        // Scoped to this module's context, so SaveChanges commits this
        // module's writes and nothing else. The application layer sees only
        // IUnitOfWork, which is what keeps EF Core out of Domain and
        // Application.
        // Keyed, so this cannot collide with another module's unit of work.
        // See IUnitOfWork for what an unkeyed registration caused.
        services.AddScoped<IdentityUnitOfWork>();
        services.AddKeyedScoped<IUnitOfWork>(UnitOfWorkKeys.Identity, (sp, _) =>
            sp.GetRequiredService<IdentityUnitOfWork>());

        // ISecurityEventRecorder is deliberately NOT registered here. This
        // module declares the interface and the Audit module supplies the
        // implementation, registered in AuditModule. That keeps the dependency
        // one-directional: Identity never references Audit, and the host is
        // the only place the two are joined.
    }

    /// <summary>
/// Does nothing on purpose.
///
/// The schema is created by db/init/001-schemas.sql, which Postgres runs when
/// the volume is first initialised. That replaced EnsureCreated, which cannot
/// support more than one module: it creates the database and its own tables,
/// and does nothing at all when the database already exists — so the second
/// module to run silently created nothing.
///
/// Keeping a second, weaker path alive here would mean the SQL file and the
/// model could drift, with the mismatch surfacing as a runtime error rather
/// than a failed build. One source of truth.
///
/// Migrations take over in Phase 9 (see docs/ARCHITECTURE.md).
/// </summary>
public void Configure(IServiceProvider services, IHostEnvironment environment)
    {
    }
}
