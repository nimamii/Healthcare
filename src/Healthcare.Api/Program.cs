using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Healthcare.Modules.Audit.Infrastructure;
using Healthcare.Modules.Catalog.Api;
using Healthcare.Modules.Geography.Infrastructure;
using Healthcare.Modules.Catalog.Infrastructure;
using Healthcare.Modules.Orders.Api;
using Healthcare.Modules.Orders.Infrastructure;
using Healthcare.Modules.Payments.Api;
using Healthcare.Modules.Payments.Infrastructure;
using Healthcare.Modules.Fulfillment.Api;
using Healthcare.Modules.Fulfillment.Infrastructure;
using Healthcare.Modules.Identity.Api;
using Healthcare.Modules.Identity.Infrastructure;
using Healthcare.Modules.Inventory.Api;
using Healthcare.Modules.Inventory.Infrastructure;
using Healthcare.SharedKernel;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Context;

// Both modules declare a RateLimitPolicies type. Aliases keep the call sites
// short while making which module owns which policy obvious at a glance —
// plain qualification would read as Identity.Contracts... or
// Catalog.Contracts..., which is easy to misread at a distance.
using CatalogPolicies = Healthcare.Modules.Catalog.Contracts.RateLimitPolicies;
using IdentityPolicies = Healthcare.Modules.Identity.Contracts.RateLimitPolicies;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, config) => config
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    // A correlation ID on every log line (§22). This is the field that makes
    // a request traceable after the fact, so it is scoped to the request
    // rather than to the process.
    //
    // Deliberately not WithMachineName(): in a container the hostname is a
    // generated ID that changes on every redeploy, so it identifies nothing.
    .Enrich.WithProperty("Application", "healthcare-api")
    .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName)
    .WriteTo.Console());

// ---------------------------------------------------------------------------
// Modules
//
// This list is the entire module registry. Adding a module means adding one
// line here — the host never scans assemblies, so an unregistered module is
// impossible to half-wire.
//
// Order matters only for Configure(): Audit registers the
// ISecurityEventRecorder that Identity's AuthService consumes, so by the time
// Identity is configured the implementation exists.
// ---------------------------------------------------------------------------
var modules = new IModule[]
{
    new AuditModule(),
    new IdentityModule(),
    new CatalogModule(),
    new InventoryModule(),
    new GeographyModule(),
    new OrdersModule(),
    new PaymentsModule(),
    new FulfillmentModule(),
};

foreach (var module in modules)
{
    module.Register(builder.Services, builder.Configuration);
}

// ---------------------------------------------------------------------------
// Authentication
//
// Deny-by-default: any endpoint without [AllowAnonymous] requires a valid
// token, and role checks run against claims this server issued (§15).
// ---------------------------------------------------------------------------
var jwtOptions = builder.Configuration
    .GetSection(JwtOptions.SectionName)
    .Get<JwtOptions>()
    ?? throw new InvalidOperationException(
        $"Configuration section '{JwtOptions.SectionName}' is missing. "
        + "Set Jwt__Issuer, Jwt__Audience and Jwt__SigningKey as environment variables (§24).");

if (jwtOptions.SigningKey.Length < 32)
{
    throw new InvalidOperationException(
        "Jwt:SigningKey must be at least 32 characters. Generate one with: "
        + "openssl rand -base64 48");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ClockSkew = TimeSpan.FromSeconds(jwtOptions.ClockSkewSeconds),

            // Without this, a token with role "PlatformAdmin" would satisfy a
            // check for "PlatformAdmin" only by accident of claim mapping.
            RoleClaimType = System.Security.Claims.ClaimTypes.Role,
            NameClaimType = System.Security.Claims.ClaimTypes.NameIdentifier,
        };

        options.Events = new JwtBearerEvents
        {
            OnChallenge = context =>
            {
                // Standard 401 with no body. The API contract here is a JSON
                // problem document (§22), not a redirect to a login page.
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/problem+json";

                return context.Response.WriteAsJsonAsync(new
                {
                    title = "احراز هویت لازم است",
                    status = StatusCodes.Status401Unauthorized,
                });
            },
        };
    });

builder.Services.AddAuthorization();

// Rate limiting (§24 requires it). Without a GlobalLimiter the middleware
// is a no-op — it only enforces whatever policy an endpoint opts into — so
// both halves are configured: a strict named policy for the unauthenticated
// auth endpoints, and a permissive global ceiling for everything else.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Named by each module, enforced here. The module decides which of its
    // endpoints are sensitive; the host decides what that means.
    options.AddPolicy(IdentityPolicies.Authentication, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));

    // Catalogue writes are administrative: no legitimate user performs them
    // in bulk, so the ceiling is far below the authenticated default. A
    // compromised admin token should not be able to rewrite the catalogue in
    // a loop.
    options.AddPolicy(CatalogPolicies.CatalogAdmin, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? httpContext.Connection.RemoteIpAddress?.ToString()
                ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.User.Identity?.IsAuthenticated == true
                ? $"user:{httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)}"
                : $"ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}",
            factory: key => new FixedWindowRateLimiterOptions
            {
                // Authenticated callers are already identified, so a higher
                // ceiling is safe and avoids throttling normal browsing.
                PermitLimit = key.StartsWith("user:", StringComparison.Ordinal) ? 300 : 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
});

// ---------------------------------------------------------------------------
// OpenAPI
// ---------------------------------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "یا علی — Healthcare Marketplace API",
        Version = "v1",
        Description = "مارکت‌پلیس تجهیزات پزشکی",
    });

    var scheme = new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "توکن دریافتی از /api/identity/login",
        Reference = new Microsoft.OpenApi.Models.OpenApiReference
        {
            Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
            Id = "Bearer",
        },
    };

    options.AddSecurityDefinition("Bearer", scheme);
    options.AddSecurityRequirement(
        new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
        {
            [scheme] = Array.Empty<string>(),
        });
});

// ---------------------------------------------------------------------------
// Health checks (§23)
// ---------------------------------------------------------------------------
builder.Services.AddHealthChecks();

var app = builder.Build();

// Attach the request id so every log entry emitted while handling a request
// carries it (§22). Serilog's LogContext scopes this, so a line written deep
// in a service is correlatable with the request that caused it.
//
// This must be the first middleware: everything registered after it writes
// logs while the property is on the stack.
app.Use(async (context, next) =>
{
    using (LogContext.PushProperty("CorrelationId", context.TraceIdentifier))
    {
        await next();
    }
});

app.UseSerilogRequestLogging();
// A bare UseExceptionHandler() throws at startup outside Development, so the
// branch is explicit. In production an unhandled exception must still produce
// a clean 500 with no stack trace or infrastructure detail (§22).
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsJsonAsync(new
        {
            type = "https://httpstatuses.com/500",
            title = "خطای داخلی سرور",
            status = StatusCodes.Status500InternalServerError,

            // The correlation id is the only handle a caller gets. It matches
            // the field on every log line, so a user-reported failure is
            // traceable without exposing anything about the internals.
            correlationId = context.TraceIdentifier,
        });
    }));
}
app.UseStatusCodePages();

app.UseSwagger();
app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "v1"));

// Order matters here. Authentication must run before the rate limiter,
// otherwise httpContext.User is still anonymous and every caller is
// throttled as if by IP — one office behind a single NAT address would then
// share one bucket.
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapAuthEndpoints();
app.MapCatalogEndpoints();
app.MapOrdersEndpoints();
app.MapPaymentsEndpoints();
app.MapFulfillmentEndpoints();
app.MapInventoryEndpoints();

foreach (var module in modules)
{
    module.Configure(app.Services, app.Environment);
}

app.Run();

/// <summary>
/// Exposed so integration tests can use <c>WebApplicationFactory</c>.
/// This type sits in the global namespace on purpose: a file-scoped
/// namespace cannot coexist with top-level statements, and moving it would
/// put the host in a namespace that none of the module code expects.
/// </summary>
public partial class Program;
