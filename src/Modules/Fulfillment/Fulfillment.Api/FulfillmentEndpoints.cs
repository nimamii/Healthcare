using Healthcare.Modules.Fulfillment.Contracts;
using Healthcare.Modules.Geography.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Healthcare.Modules.Fulfillment.Api;

public static class FulfillmentEndpoints
{
    public static void MapFulfillmentEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/fulfillment").WithTags("Fulfillment");
        group.MapGet("/skus/{skuId:guid}/suppliers", RankSuppliersAsync).AllowAnonymous().WithName("RankSuppliers");
        group.MapPost("/reserve", ReserveAsync).RequireAuthorization().WithName("ReserveStock");
    }

    private static async Task<IResult> RankSuppliersAsync(Guid skuId, double latitude, double longitude, IFulfillmentService service, CancellationToken ct)
    {
        var result = await service.RankSuppliersAsync(skuId, new GeoPoint(latitude, longitude), ct);
        return result.IsSuccess ? Results.Ok(result.Value) : Results.Problem(detail: result.ErrorMessage, statusCode: 400);
    }

    private static async Task<IResult> ReserveAsync(ReserveRequest request, IFulfillmentService service, CancellationToken ct)
    {
        var result = await service.ReserveAsync(request.OrderId, request.SkuId, request.SupplierId, request.Quantity, ct);
        return result.IsSuccess ? Results.Ok(new { ReservationId = result.Value }) : Results.Problem(detail: result.ErrorMessage, statusCode: 400);
    }
}

public sealed record ReserveRequest(Guid OrderId, Guid SkuId, Guid SupplierId, int Quantity);
