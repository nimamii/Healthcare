using Healthcare.Modules.Orders.Application;
using Healthcare.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Healthcare.Modules.Orders.Api;

public static class OrdersEndpoints
{
    private const string OrderRole = "Customer";

    public static void MapOrdersEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/orders").WithTags("Orders");

        group.MapPost("/", CreateOrderAsync)
             .RequireAuthorization(new AuthorizeAttribute { Roles = OrderRole })
             .WithName("CreateOrder")
             .WithSummary("ایجاد سفارش جدید");

        group.MapPost("/{orderId:guid}/lines", AddLineAsync)
             .RequireAuthorization(new AuthorizeAttribute { Roles = OrderRole })
             .WithName("AddOrderLine")
             .WithSummary("افزودن آیتم با ردیابی شماره بچ و سریال به سفارش");

        group.MapPost("/{orderId:guid}/b2b-terms", SetB2BTermsAsync)
             .RequireAuthorization(new AuthorizeAttribute { Roles = OrderRole })
             .WithName("SetB2BTerms")
             .WithSummary("ثبت شماره سفارش خرید شرکتی (PO) و شرایط تسویه اعتباری (Net-30/60)");

        group.MapPost("/{orderId:guid}/verify-compliance", VerifyComplianceAsync)
             .RequireAuthorization(new AuthorizeAttribute { Roles = OrderRole })
             .WithName("VerifyCompliance")
             .WithSummary("ثبت و احراز شماره نظام پزشکی یا مجوز مرکز درمانی");

        group.MapPost("/{orderId:guid}/payment-pending", MarkPaymentPendingAsync)
             .RequireAuthorization(new AuthorizeAttribute { Roles = OrderRole })
             .WithName("MarkPaymentPending")
             .WithSummary("علامت‌گذاری سفارش به‌عنوان در انتظار پرداخت");

        group.MapPost("/{orderId:guid}/paid", MarkPaidAsync)
             .RequireAuthorization(new AuthorizeAttribute { Roles = OrderRole })
             .WithName("MarkPaid")
             .WithSummary("علامت‌گذاری سفارش به‌عنوان پرداخت‌شده");

        group.MapPost("/{orderId:guid}/fulfillment-pending", MarkFulfillmentPendingAsync)
             .RequireAuthorization(new AuthorizeAttribute { Roles = OrderRole })
             .WithName("MarkFulfillmentPending")
             .WithSummary("علامت‌گذاری سفارش به‌عنوان در انتظار تحویل");

        group.MapPost("/{orderId:guid}/confirm", ConfirmAsync)
             .RequireAuthorization(new AuthorizeAttribute { Roles = OrderRole })
             .WithName("ConfirmOrder")
             .WithSummary("تأیید سفارش");

        group.MapPost("/{orderId:guid}/cancel", CancelAsync)
             .RequireAuthorization(new AuthorizeAttribute { Roles = OrderRole })
             .WithName("CancelOrder")
             .WithSummary("لغو سفارش");
    }

    private static async Task<IResult> CreateOrderAsync(
        CreateOrderRequest request,
        IOrderService orders,
        CancellationToken ct)
    {
        var result = await orders.CreateOrder(request.CustomerId, request.SupplierId, DateTimeOffset.UtcNow, ct);

        return result.IsSuccess
            ? Results.Created($"/api/orders/{result.Value.Id}", result.Value)
            : Results.Problem(title: "ایجاد سفارش ناموفق بود", detail: result.ErrorMessage, statusCode: 400);
    }

    private static async Task<IResult> AddLineAsync(
        Guid orderId,
        AddLineRequest request,
        IOrderService orders,
        CancellationToken ct)
    {
        var result = await orders.AddLine(
            orderId,
            request.SkuId,
            request.Quantity,
            request.UnitPrice,
            request.Currency,
            DateTimeOffset.UtcNow,
            request.LotNumber,
            request.SerialNumber,
            request.ExpiryDate,
            ct);

        return result.IsSuccess
            ? Results.NoContent()
            : Results.Problem(title: "افزودن آیتم ناموفق بود", detail: result.ErrorMessage, statusCode: 400);
    }

    private static async Task<IResult> SetB2BTermsAsync(
        Guid orderId,
        SetB2BTermsRequest request,
        IOrderService orders,
        CancellationToken ct)
    {
        var result = await orders.SetB2BTerms(orderId, request.PurchaseOrderNumber, request.PaymentTerms, DateTimeOffset.UtcNow, ct);

        return result.IsSuccess
            ? Results.NoContent()
            : Results.Problem(title: "ثبت شرایط خرید ناموفق بود", detail: result.ErrorMessage, statusCode: 400);
    }

    private static async Task<IResult> VerifyComplianceAsync(
        Guid orderId,
        VerifyComplianceRequest request,
        IOrderService orders,
        CancellationToken ct)
    {
        var result = await orders.VerifyCompliance(orderId, request.MedicalLicenseNumber, DateTimeOffset.UtcNow, ct);

        return result.IsSuccess
            ? Results.NoContent()
            : Results.Problem(title: "احراز مجوز ناموفق بود", detail: result.ErrorMessage, statusCode: 400);
    }

    private static async Task<IResult> MarkPaymentPendingAsync(
        Guid orderId,
        IOrderService orders,
        CancellationToken ct)
    {
        var result = await orders.MarkPaymentPending(orderId, DateTimeOffset.UtcNow, ct);

        return result.IsSuccess
            ? Results.NoContent()
            : Results.Problem(title: "علامت‌گذاری ناموفق بود", detail: result.ErrorMessage, statusCode: 400);
    }

    private static async Task<IResult> MarkPaidAsync(
        Guid orderId,
        IOrderService orders,
        CancellationToken ct)
    {
        var result = await orders.MarkPaid(orderId, DateTimeOffset.UtcNow, ct);

        return result.IsSuccess
            ? Results.NoContent()
            : Results.Problem(title: "علامت‌گذاری ناموفق بود", detail: result.ErrorMessage, statusCode: 400);
    }

    private static async Task<IResult> MarkFulfillmentPendingAsync(
        Guid orderId,
        IOrderService orders,
        CancellationToken ct)
    {
        var result = await orders.MarkFulfillmentPending(orderId, DateTimeOffset.UtcNow, ct);

        return result.IsSuccess
            ? Results.NoContent()
            : Results.Problem(title: "علامت‌گذاری ناموفق بود", detail: result.ErrorMessage, statusCode: 400);
    }

    private static async Task<IResult> ConfirmAsync(
        Guid orderId,
        IOrderService orders,
        CancellationToken ct)
    {
        var result = await orders.Confirm(orderId, DateTimeOffset.UtcNow, ct);

        return result.IsSuccess
            ? Results.NoContent()
            : Results.Problem(title: "تأیید ناموفق بود", detail: result.ErrorMessage, statusCode: 400);
    }

    private static async Task<IResult> CancelAsync(
        Guid orderId,
        IOrderService orders,
        CancellationToken ct)
    {
        var result = await orders.Cancel(orderId, DateTimeOffset.UtcNow, ct);

        return result.IsSuccess
            ? Results.NoContent()
            : Results.Problem(title: "لغو ناموفق بود", detail: result.ErrorMessage, statusCode: 400);
    }
}

public sealed record CreateOrderRequest(Guid CustomerId, Guid SupplierId);

public sealed record AddLineRequest(
    Guid SkuId,
    int Quantity,
    decimal UnitPrice,
    string Currency,
    string? LotNumber = null,
    string? SerialNumber = null,
    DateTimeOffset? ExpiryDate = null);

public sealed record SetB2BTermsRequest(string PurchaseOrderNumber, string PaymentTerms);

public sealed record VerifyComplianceRequest(string MedicalLicenseNumber);
