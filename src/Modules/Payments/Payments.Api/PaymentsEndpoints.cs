using Healthcare.Modules.Payments.Application;
using Healthcare.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Healthcare.Modules.Payments.Api;

public static class PaymentsEndpoints
{
    private const string PaymentRole = "Customer";

    public static void MapPaymentsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/payments").WithTags("Payments");

        group.MapPost("/", CreatePaymentAsync)
             .RequireAuthorization(new AuthorizeAttribute { Roles = PaymentRole })
             .WithName("CreatePayment")
             .WithSummary("ایجاد پرداخت جدید");

        group.MapPost("/{paymentId:guid}/refresh", RefreshStatusAsync)
             .RequireAuthorization(new AuthorizeAttribute { Roles = PaymentRole })
             .WithName("RefreshPaymentStatus")
             .WithSummary("به‌روزرسانی وضعیت پرداخت از ارائه‌دهنده");

        group.MapPost("/{paymentId:guid}/void", VoidAsync)
             .RequireAuthorization(new AuthorizeAttribute { Roles = PaymentRole })
             .WithName("VoidPayment")
             .WithSummary("لغو پرداخت");
    }

    private static async Task<IResult> CreatePaymentAsync(
        CreatePaymentRequest request,
        IPaymentService payments,
        CancellationToken ct)
    {
        var result = await payments.CreatePaymentAsync(
            request.OrderId, request.Amount, request.Currency, DateTimeOffset.UtcNow);

        return result.IsSuccess
            ? Results.Created($"/api/payments/{result.Value.Id}", result.Value)
            : Results.Problem(title: "ایجاد پرداخت ناموفق بود", detail: result.ErrorMessage, statusCode: 400);
    }

    private static async Task<IResult> RefreshStatusAsync(
        Guid paymentId,
        IPaymentService payments,
        CancellationToken ct)
    {
        var result = await payments.RefreshStatusAsync(paymentId, DateTimeOffset.UtcNow);

        return result.IsSuccess
            ? Results.NoContent()
            : Results.Problem(title: "به‌روزرسانی وضعیت ناموفق بود", detail: result.ErrorMessage, statusCode: 400);
    }

    private static async Task<IResult> VoidAsync(
        Guid paymentId,
        IPaymentService payments,
        CancellationToken ct)
    {
        var result = await payments.VoidAsync(paymentId, DateTimeOffset.UtcNow);

        return result.IsSuccess
            ? Results.NoContent()
            : Results.Problem(title: "لغو پرداخت ناموفق بود", detail: result.ErrorMessage, statusCode: 400);
    }
}

// --- Requests ----------------------------------------------------------------

public sealed record CreatePaymentRequest(Guid OrderId, decimal Amount, string Currency);
