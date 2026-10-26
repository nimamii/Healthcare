using Healthcare.Modules.Inventory.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Healthcare.Modules.Inventory.Api;

public static class InventoryEndpoints
{
    public static void MapInventoryEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/inventory").WithTags("Inventory");

        group.MapGet("/skus/{skuId:guid}", GetStockForSkuAsync)
             .AllowAnonymous()
             .WithName("GetStockForSku")
             .WithSummary("دریافت موجودی تأمین‌کنندگان برای یک SKU");

        group.MapPost("/adjust", AdjustStockAsync)
             .RequireAuthorization()
             .WithName("AdjustStock")
             .WithSummary("تنظیم موجودی قابل فروش یک SKU توسط تأمین‌کننده یا ادمین");
    }

    private static async Task<IResult> GetStockForSkuAsync(
        Guid skuId,
        IInventoryReader reader,
        CancellationToken ct)
    {
        var rows = await reader.GetForSkuAsync(skuId, ct);
        return Results.Ok(rows);
    }

    private static async Task<IResult> AdjustStockAsync(
        AdjustStockRequest request,
        IInventoryService inventory,
        CancellationToken ct)
    {
        var result = await inventory.AdjustAsync(
            new AdjustStockCommand(request.SkuId, request.SupplierId, request.NewAvailableQuantity),
            ct);

        return result.IsSuccess
            ? Results.NoContent()
            : Results.Problem(
                title: "تنظیم موجودی ناموفق بود",
                detail: result.ErrorMessage,
                statusCode: 400);
    }
}

public sealed record AdjustStockRequest(Guid SkuId, Guid SupplierId, int NewAvailableQuantity);
