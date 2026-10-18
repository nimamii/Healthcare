using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

/// <summary>
/// A self-contained module. The composition root discovers these and
/// calls <see cref="Register"/> / <see cref="Configure"/> exactly once.
///
/// This is the only way a module gets into the container. Nothing scans
/// assemblies on its own — a module that forgets to implement this is a
/// module that silently does nothing, which is exactly the failure we want
/// visible at startup rather than at first request.
/// </summary>
namespace Healthcare.SharedKernel;

public interface IModule
{
    /// <summary>Stable identifier, also used as the database schema name.</summary>
    string Name { get; }

    /// <summary>
    /// Register this module's own services. Implementations must NOT register
    /// another module's services — cross-module wiring belongs in the host.
    /// </summary>
    void Register(IServiceCollection services, IConfiguration configuration);

    /// <summary>
    /// Run after all modules are registered. Use for cross-cutting setup that
    /// needs the full container, such as applying migrations.
    /// </summary>
    void Configure(IServiceProvider services, IHostEnvironment environment);
}
