using System.Security.Claims;
using FluentValidation;
using FluentValidation.Results;
using Healthcare.Modules.Catalog.Application;
using Healthcare.Modules.Catalog.Contracts;
using Healthcare.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

// This project uses Microsoft.NET.Sdk with an explicit FrameworkReference, not
// the Web SDK, so none of the ASP.NET Core namespaces are implicit.
namespace Healthcare.Modules.Catalog.Api;

public static class CatalogEndpoints
{
    /// <summary>
    /// Reads are open to anyone: a catalogue is public information and the
    /// customer journey starts before login.
    ///
    /// Writes require PlatformAdmin. Catalog staff are the only people who may
    /// change what the marketplace sells, and the spec requires authorization
    /// to be enforced server-side rather than inferred from the client (§15).
    /// </summary>
    private const string CatalogAdminRole = "PlatformAdmin";

    public static void MapCatalogEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/catalog").WithTags("Catalog");

        // --- Reads ------------------------------------------------------------

        group.MapGet("/products", ListProductsAsync)
             .AllowAnonymous()
             .WithName("ListProducts")
             .WithSummary("فهرست محصولات فعال");

        group.MapGet("/products/{id:guid}", GetProductAsync)
             .AllowAnonymous()
             .WithName("GetProduct")
             .WithSummary("دریافت یک محصول");

        group.MapGet("/skus/by-code/{code}", GetSkuByCodeAsync)
             .AllowAnonymous()
             .WithName("GetSkuByCode")
             .WithSummary("جست‌وجوی SKU بر اساس کد");

        group.MapGet("/skus/{id:guid}/listings", GetListingsAsync)
             .AllowAnonymous()
             .WithName("GetSkuListings")
             .WithSummary("فهرست تأمین‌کنندگان یک SKU");

        // --- Writes -----------------------------------------------------------

        group.MapPost("/categories", CreateCategoryAsync)
             .RequireAuthorization(new AuthorizeAttribute { Roles = CatalogAdminRole })
             .RequireRateLimiting(Catalog.Contracts.RateLimitPolicies.CatalogAdmin)
             .WithName("CreateCategory")
             .WithSummary("ایجاد دسته‌بندی");

        group.MapPost("/products", CreateProductAsync)
             .RequireAuthorization(new AuthorizeAttribute { Roles = CatalogAdminRole })
             .RequireRateLimiting(Catalog.Contracts.RateLimitPolicies.CatalogAdmin)
             .WithName("CreateProduct")
             .WithSummary("ایجاد محصول");

        group.MapPost("/skus", CreateSkuAsync)
             .RequireAuthorization(new AuthorizeAttribute { Roles = CatalogAdminRole })
             .RequireRateLimiting(Catalog.Contracts.RateLimitPolicies.CatalogAdmin)
             .WithName("CreateSku")
             .WithSummary("ایجاد SKU برای محصول");

        group.MapPost("/supplier-products", LinkSupplierProductAsync)
             .RequireAuthorization(new AuthorizeAttribute { Roles = CatalogAdminRole })
             .RequireRateLimiting(Catalog.Contracts.RateLimitPolicies.CatalogAdmin)
             .WithName("LinkSupplierProduct")
             .WithSummary("اتصال یک SKU به تأمین‌کننده");
    }

    private static async Task<IResult> ListProductsAsync(
        Guid? categoryId,
        IProductCatalog catalog,
        CancellationToken ct)
    {
        var products = await catalog.ListProductsAsync(categoryId, ct);
        return Results.Ok(products);
    }

    private static async Task<IResult> GetProductAsync(
        Guid id,
        IProductCatalog catalog,
        CancellationToken ct)
    {
        var product = await catalog.FindProductAsync(id, ct);

        return product is null
            ? Results.NotFound()
            : Results.Ok(product);
    }

    private static async Task<IResult> GetSkuByCodeAsync(
        string code,
        IProductCatalog catalog,
        CancellationToken ct)
    {
        var sku = await catalog.FindSkuByCodeAsync(code, ct);

        return sku is null
            ? Results.NotFound()
            : Results.Ok(sku);
    }

    private static async Task<IResult> GetListingsAsync(
        Guid id,
        IProductCatalog catalog,
        CancellationToken ct)
    {
        var listings = await catalog.GetListingsAsync(id, ct);

        return Results.Ok(listings);
    }

    private static async Task<IResult> CreateCategoryAsync(
        CreateCategoryRequest request,
        ICatalogService catalog,
        CancellationToken ct)
    {
        var validation = new CreateCategoryRequestValidator().Validate(request);

        if (!validation.IsValid)
        {
            return Results.ValidationProblem(ToErrorDictionary(validation));
        }

        var result = await catalog.CreateCategoryAsync(
            new CreateCategoryCommand(request.Name, request.Slug, request.ParentId),
            ct);

        return ToResult(result);
    }

    private static async Task<IResult> CreateProductAsync(
        CreateProductRequest request,
        ICatalogService catalog,
        CancellationToken ct)
    {
        var validation = new CreateProductRequestValidator().Validate(request);

        if (!validation.IsValid)
        {
            return Results.ValidationProblem(ToErrorDictionary(validation));
        }

        var result = await catalog.CreateProductAsync(
            new CreateProductCommand(
                request.Name,
                request.Slug,
                request.Description,
                request.CategoryId,
                request.RequiresPrescription),
            ct);

        return ToResult(result);
    }

    private static async Task<IResult> CreateSkuAsync(
        CreateSkuRequest request,
        ICatalogService catalog,
        CancellationToken ct)
    {
        var validation = new CreateSkuRequestValidator().Validate(request);

        if (!validation.IsValid)
        {
            return Results.ValidationProblem(ToErrorDictionary(validation));
        }

        var result = await catalog.CreateSkuAsync(
            new CreateSkuCommand(request.ProductId, request.VariantDescription),
            ct);

        return ToResult(result);
    }

    private static async Task<IResult> LinkSupplierProductAsync(
        LinkSupplierProductRequest request,
        ICatalogService catalog,
        CancellationToken ct)
    {
        var validation = new LinkSupplierProductRequestValidator().Validate(request);

        if (!validation.IsValid)
        {
            return Results.ValidationProblem(ToErrorDictionary(validation));
        }

        var result = await catalog.LinkSupplierProductAsync(
            new LinkSupplierProductCommand(
                request.SkuId,
                request.SupplierId,
                request.SupplierSku,
                request.ListPrice,
                request.Currency),
            ct);

        return result.IsSuccess
            ? Results.NoContent()
            : Results.Problem(title: "عملیات ناموفق بود", detail: result.ErrorMessage, statusCode: 400);
    }

    /// <summary>
    /// FluentValidation 11 removed the instance ToDictionary helper, and the
    /// ASP.NET integration package is not otherwise needed here.
    /// </summary>
    private static Dictionary<string, string[]> ToErrorDictionary(ValidationResult result) =>
        result.Errors
            .GroupBy(failure => failure.PropertyName)
            .ToDictionary(
                group => group.Key,
                group => group.Select(failure => failure.ErrorMessage).ToArray());

    /// <summary>
    /// Wraps a created id in an object rather than returning a bare GUID.
    ///
    /// A bare string body is valid JSON but a poor contract: a client cannot
    /// tell it apart from any other string, and adding a field later becomes
    /// a breaking change. <c>{ "id": ... }</c> extends without ambiguity.
    /// </summary>
    private static IResult ToResult<T>(Result<T> result) =>
        result.IsSuccess
            ? Results.Created($"/api/catalog/{result.Value}", new { Id = result.Value })
            : Results.Problem(title: "عملیات ناموفق بود", detail: result.ErrorMessage, statusCode: 400);
}

// --- Requests ----------------------------------------------------------------

public sealed record CreateCategoryRequest(string Name, string Slug, Guid? ParentId);

public sealed record CreateProductRequest(
    string Name,
    string Slug,
    string? Description,
    Guid CategoryId,
    bool RequiresPrescription);

public sealed record CreateSkuRequest(Guid ProductId, string? VariantDescription);

public sealed record LinkSupplierProductRequest(
    Guid SkuId,
    Guid SupplierId,
    string SupplierSku,
    decimal? ListPrice,
    string Currency);

// --- Validation --------------------------------------------------------------

public sealed class CreateCategoryRequestValidator : AbstractValidator<CreateCategoryRequest>
{
    public CreateCategoryRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("نام دسته‌بندی الزامی است.")
            .MaximumLength(200).WithMessage("نام نباید بیش از ۲۰۰ کاراکتر باشد.");

        RuleFor(x => x.Slug).NotEmpty().WithMessage("شناسه یکتا الزامی است.")
            .MaximumLength(200)
            .Matches("^[a-z0-9-]+$").WithMessage("شناسه یکتا فقط می‌تواند شامل حروف کوچک انگلیسی، عدد و خط تیره باشد.");
    }
}

public sealed class CreateProductRequestValidator : AbstractValidator<CreateProductRequest>
{
    public CreateProductRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("نام محصول الزامی است.")
            .MaximumLength(300);

        RuleFor(x => x.Slug).NotEmpty().WithMessage("شناسه یکتا الزامی است.")
            .MaximumLength(300)
            .Matches("^[a-z0-9-]+$").WithMessage("شناسه یکتا فقط می‌تواند شامل حروف کوچک انگلیسی، عدد و خط تیره باشد.");

        RuleFor(x => x.Description).MaximumLength(4000);

        RuleFor(x => x.CategoryId).NotEmpty().WithMessage("دسته‌بندی الزامی است.");
    }
}

public sealed class CreateSkuRequestValidator : AbstractValidator<CreateSkuRequest>
{
    public CreateSkuRequestValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty().WithMessage("محصول الزامی است.");

        RuleFor(x => x.VariantDescription).MaximumLength(300);
    }
}

public sealed class LinkSupplierProductRequestValidator : AbstractValidator<LinkSupplierProductRequest>
{
    public LinkSupplierProductRequestValidator()
    {
        RuleFor(x => x.SkuId).NotEmpty().WithMessage("SKU الزامی است.");
        RuleFor(x => x.SupplierId).NotEmpty().WithMessage("تأمین‌کننده الزامی است.");

        RuleFor(x => x.SupplierSku).NotEmpty().WithMessage("کد تأمین‌کننده الزامی است.")
            .MaximumLength(128);

        // Null means "not published", which is different from zero. Rejecting
        // zero here stops a data-entry mistake from becoming a free item.
        RuleFor(x => x.ListPrice)
            .GreaterThan(0).WithMessage("قیمت باید بزرگ‌تر از صفر باشد.")
            .When(x => x.ListPrice.HasValue);

        RuleFor(x => x.Currency)
            .Length(3).WithMessage("کد ارز باید سه حرف باشد.")
            .Matches("^[A-Za-z]{3}$");
    }
}