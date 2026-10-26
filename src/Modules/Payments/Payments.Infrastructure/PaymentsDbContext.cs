using Healthcare.Modules.Payments.Application;
using Healthcare.Modules.Payments.Domain;
using Healthcare.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Healthcare.Modules.Payments.Infrastructure;

/// <summary>
/// Payments این context و schema پرداخت‌ها را مدیریت می‌کند.
/// </summary>
public class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : DbContext(options)
{
    public const string Schema = "payments";

    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema(Schema);

        builder.Entity<Payment>(entity =>
        {
            entity.ToTable("payments");
            entity.HasKey(p => p.Id);

            entity.Property(p => p.OrderId).IsRequired();
            entity.Property(p => p.Amount).HasPrecision(18, 4).IsRequired();
            entity.Property(p => p.Currency).HasMaxLength(3).IsRequired();
            entity.Property(p => p.Status).IsRequired();
            entity.Property(p => p.ProviderTransactionId).HasMaxLength(200);
            entity.Property(p => p.CreatedAt).IsRequired();
            entity.Property(p => p.UpdatedAt).IsRequired();

            entity.HasIndex(p => p.OrderId);
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
/// ماژول زیرساخت برای پرداخت‌ها.
/// </summary>
public sealed class PaymentsModule : IModule
{
    public string Name => "payments";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Default is required. Set it via environment variable (§24).");

        services.AddDbContext<PaymentsDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<PaymentsUnitOfWork>();

        // درگاه پرداخت: در محیط توسعه از Mock استفاده می‌کنیم.
        // در Production باید ارائه‌دهنده واقعی ثبت شود.
        services.AddScoped<IPaymentGateway, MockPaymentGateway>();

        // Keyed تا با unit of work ماژول دیگری تداخل نداشته باشد.
        services.AddKeyedScoped<IUnitOfWork>(UnitOfWorkKeys.Payments, (sp, _) =>
            sp.GetRequiredService<PaymentsUnitOfWork>());
    }

    public void Configure(IServiceProvider services, IHostEnvironment environment)
    {
    }
}

/// <summary>
/// پیاده‌سازی مخزن برای پرداخت‌ها.
/// </summary>
public sealed class PaymentRepository(PaymentsDbContext db) : IPaymentRepository
{
    public Payment? FindById(Guid id) =>
        db.Payments.FirstOrDefault(p => p.Id == id);

    public void Add(Payment payment) => db.Payments.Add(payment);

    public void Update(Payment payment) => db.Payments.Update(payment);
}

/// <summary>
/// Unit of work برای پرداخت‌ها.
/// </summary>
public sealed class PaymentsUnitOfWork(PaymentsDbContext db) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}

/// <summary>
/// درگاه پرداخت Mock برای توسعه و تست.
/// </summary>
/// <remarks>
/// این درگاه هیچ درخواست واقعی ارسال نمی‌کند. همیشه موفق است و یک شناسه تراکنش تصادفی برمی‌گرداند.
/// در Production باید با ارائه‌دهنده واقعی جایگزین شود.
/// </remarks>
public sealed class MockPaymentGateway : IPaymentGateway
{
    public Result<string> Authorize(Guid paymentId, decimal amount, string currency)
    {
        // همیشه موفق — یک شناسه تراکنش تصادفی برمی‌گردانیم.
        return Result<string>.Success($"mock_tx_{paymentId:N}");
    }

    public Result<GatewayStatus> GetStatus(Guid paymentId)
    {
        // همیشه Settled برمی‌گردانیم تا تست‌ها ساده باشند.
        return Result<GatewayStatus>.Success(GatewayStatus.Settled);
    }

    public Result Void(Guid paymentId)
    {
        return Result.Success();
    }
}
