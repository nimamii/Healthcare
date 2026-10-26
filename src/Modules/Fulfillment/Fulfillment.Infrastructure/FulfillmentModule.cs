using Healthcare.Modules.Fulfillment.Application;
using Healthcare.Modules.Fulfillment.Contracts;
using Healthcare.SharedKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Healthcare.Modules.Fulfillment.Infrastructure;

public sealed class FulfillmentModule : IModule
{
    public string Name => "fulfillment";
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IFulfillmentService, FulfillmentService>();
    }
    public void Configure(IServiceProvider services, IHostEnvironment environment) { }
}
