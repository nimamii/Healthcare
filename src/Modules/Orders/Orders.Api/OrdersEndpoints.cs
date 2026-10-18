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
             .WithSummary("افزودن آیتم به سفارش");

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
        var result = await orders.CreateOrder(request.CustomerId, request.SupplierId, DateTimeOffset.UtcNow);

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
            orderId, request.SkuId, request.Quantity, request.UnitPrice, request.Currency, DateTimeOffset.UtcNow);

        return result.IsSuccess
            ? Results.NoContent()
            : Results.Problem(title: "افزودن آیتم ناموفق بود", detail: result.ErrorMessage, statusCode: 400);
    }

    private static async Task<IResult> MarkPaymentPendingAsync(
        Guid orderId,
        IOrderService orders,
        CancellationToken ct)
    {
        var result = await orders.MarkPaymentPending(orderId, DateTimeOffset.UtcNow);

        return result.IsSuccess
            ? Results.NoContent()
            : Results.Problem(title: "علامت‌گذاری ناموفق بود", detail: result.ErrorMessage, statusCode: 400);
    }

    private static async Task<IResult> MarkPaidAsync(
        Guid orderId,
        IOrderService orders,
        CancellationToken ct)
    {
        var result = await orders.MarkPaid(orderId, DateTimeOffset.UtcNow);

        return result.IsSuccess
            ? Results.NoContent()
            : Results.Problem(title: "علامت‌گذاری ناموفق بود", detail: result.ErrorMessage, statusCode: 400);
    }

    private static async Task<IResult> MarkFulfillmentPendingAsync(
        Guid orderId,
        IOrderService orders,
        CancellationToken ct)
    {
        var result = await orders.MarkFulfillmentPending(orderId, DateTimeOffset.UtcNow);

        return result.IsSuccess
            ? Results.NoContent()
            : Results.Problem(title: "علامت‌گذاری ناموفق بود", detail: result.ErrorMessage, statusCode: 400);
    }

    private static async Task<IResult> ConfirmAsync(
        Guid orderId,
        IOrderService orders,
        CancellationToken ct)
    {
        var result = await orders.Confirm(orderId, DateTimeOffset.UtcNow);

        return result.IsSuccess
            ? Results.NoContent()
            : Results.Problem(title: "تأیید ناموفق بود", detail: result.ErrorMessage, statusCode: 400);
    }

    private static async Task<IResult> CancelAsync(
        Guid orderId,
        IOrderService orders,
        CancellationToken ct)
    {
        var result = await orders.Cancel(orderId, DateTimeOffset.UtcNow);

        return result.IsSuccess
            ? Results.NoContent()
            : Results.Problem(title: "لغو ناموفق بود", detail: result.ErrorMessage, statusCode: 400);
    }
}

// --- Requests ----------------------------------------------------------------

public sealed record CreateOrderRequest(Guid CustomerId, Guid SupplierId);

public sealed record AddLineRequest(Guid SkuId, int Quantity, decimal UnitPrice, string Currency);
