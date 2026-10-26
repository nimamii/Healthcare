using Healthcare.Modules.Orders.Application;
using Healthcare.Modules.Orders.Domain;
using Healthcare.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Healthcare.Modules.Orders.Infrastructure;

/// <summary>
/// Orders owns this context and the orders schema.
/// </summary>
public class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
{
    public const string Schema = "orders";

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderLine> OrderLines => Set<OrderLine>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema(Schema);

        builder.Entity<Order>(entity =>
        {
            entity.ToTable("orders");
            entity.HasKey(o => o.Id);

            entity.Property(o => o.CustomerId).IsRequired();
            entity.Property(o => o.SupplierId).IsRequired();
            entity.Property(o => o.Status).IsRequired();
            entity.Property(o => o.CreatedAt).IsRequired();
            entity.Property(o => o.UpdatedAt).IsRequired();

            entity.HasMany(o => o.Lines)
                .WithOne()
                .HasForeignKey("OrderId")
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<OrderLine>(entity =>
        {
            entity.ToTable("order_lines");
            entity.HasKey(ol => ol.Id);

            entity.Property(ol => ol.SkuId).IsRequired();
            entity.Property(ol => ol.Quantity).IsRequired();
            entity.Property(ol => ol.UnitPrice).HasPrecision(18, 4).IsRequired();
            entity.Property(ol => ol.Currency).HasMaxLength(3).IsRequired();
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

/// <summary>
/// Infrastructure module for orders.
/// </summary>
public sealed class OrdersModule : IModule
{
    public string Name => "orders";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Default is required. Set it via environment variable (§24).");

        services.AddDbContext<OrdersDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<OrdersUnitOfWork>();

        // Keyed, so it cannot collide with another module's unit of work.
        services.AddKeyedScoped<IUnitOfWork>(UnitOfWorkKeys.Orders, (sp, _) =>
            sp.GetRequiredService<OrdersUnitOfWork>());
    }

    public void Configure(IServiceProvider services, IHostEnvironment environment)
    {
    }
}

/// <summary>
/// Repository implementation for orders.
/// </summary>
public sealed class OrderRepository(OrdersDbContext db) : IOrderRepository
{
    public Order? FindById(Guid id) =>
        db.Orders.Include(o => o.Lines).FirstOrDefault(o => o.Id == id);

    public void Add(Order order) => db.Orders.Add(order);

    public void Update(Order order) => db.Orders.Update(order);
}

/// <summary>
/// Unit of work for orders.
/// </summary>
public sealed class OrdersUnitOfWork(OrdersDbContext db) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
