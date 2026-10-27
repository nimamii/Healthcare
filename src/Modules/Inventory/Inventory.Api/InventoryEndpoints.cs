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

        group.MapPost("/batch-adjust", BatchAdjustStockAsync)
             .RequireAuthorization()
             .WithName("BatchAdjustStock")
             .WithSummary("همگام‌سازی دسته‌ای موجودی چند کالا از طریق فایل اکسل/CSV یا وب‌هوک ERP");
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

    private static async Task<IResult> BatchAdjustStockAsync(
        BatchAdjustStockRequest request,
        IInventoryService inventory,
        CancellationToken ct)
    {
        if (request.Items == null || request.Items.Count == 0)
        {
            return Results.BadRequest(new { error = "فهرست اقلام موجودی نمی‌تواند خالی باشد." });
        }

        var results = new List<object>();
        foreach (var item in request.Items)
        {
            var res = await inventory.AdjustAsync(
                new AdjustStockCommand(item.SkuId, item.SupplierId, item.NewAvailableQuantity),
                ct);

            results.Add(new
            {
                skuId = item.SkuId,
                supplierId = item.SupplierId,
                quantity = item.NewAvailableQuantity,
                isSuccess = res.IsSuccess,
                errorMessage = res.ErrorMessage
            });
        }

        return Results.Ok(new
        {
            totalProcessed = request.Items.Count,
            sourceSystem = request.SourceSystem ?? "ERP_BATCH_CSV",
            items = results
        });
    }
}

public sealed record AdjustStockRequest(Guid SkuId, Guid SupplierId, int NewAvailableQuantity);

public sealed record BatchAdjustStockRequest(
    string? SourceSystem,
    IReadOnlyCollection<AdjustStockRequest> Items);
