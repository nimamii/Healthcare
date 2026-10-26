using Healthcare.Modules.Inventory.Contracts;
using Healthcare.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Healthcare.Modules.Inventory.Infrastructure;

public sealed class InventoryModule : IModule
{
    public string Name => "inventory";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Default is required. Set it via environment variable (§24).");

        services.AddDbContext<InventoryDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddScoped<InventoryUnitOfWork>();

        // Keyed, so this cannot collide with another module's unit of work.
        services.AddKeyedScoped<IUnitOfWork>(UnitOfWorkKeys.Inventory, (sp, _) =>
            sp.GetRequiredService<InventoryUnitOfWork>());

        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<IInventoryReader, InventoryReader>();
    }

    /// <summary>
    /// Does nothing on purpose: the schema comes from db/init/001-schemas.sql,
    /// which Postgres runs when the volume is first created. The same reason
    /// as the other modules — DbContext.EnsureCreated cannot support more than
    /// one module.
    /// </summary>
    public void Configure(IServiceProvider services, IHostEnvironment environment)
    {
    }
}

/// <summary>
/// Read side exposed to other modules through Contracts.
/// </summary>
public sealed class InventoryReader(InventoryDbContext db) : IInventoryReader
{
    public async Task<StockLevel?> GetAsync(Guid skuId, Guid supplierId, CancellationToken ct)
    {
        // Projected into a nullable holder so "no row" and "a row with zero
        // stock" are distinguishable. StockLevel is a struct, so returning it
        // directly would make an absent row look like an empty one — and an
        // empty one looks like "in stock, quantity zero", which would let a
        // caller proceed when it should not.
        var row = await db.StockItems
            .AsNoTracking()
            .Where(s => s.SkuId == skuId && s.SupplierId == supplierId)
            .Select(s => new StockLevel?(new StockLevel(
                s.SkuId, s.SupplierId, s.AvailableQuantity, s.ReservedQuantity, s.Version)))
            .FirstOrDefaultAsync(ct);

        return row;
    }

    public async Task<IReadOnlyCollection<StockLevel>> GetForSkuAsync(Guid skuId, CancellationToken ct)
    {
        var rows = await db.StockItems
            .AsNoTracking()
            .Where(s => s.SkuId == skuId)
            .Select(s => new StockLevel(
                s.SkuId, s.SupplierId, s.AvailableQuantity, s.ReservedQuantity, s.Version))
            .ToListAsync(ct);

        return rows;
    }
}