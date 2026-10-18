using FluentAssertions;
using Healthcare.Modules.Catalog.Contracts;
using Healthcare.Modules.Fulfillment.Application;
using Healthcare.Modules.Fulfillment.Contracts;
using Healthcare.Modules.Geography.Contracts;
using Healthcare.Modules.Inventory.Contracts;
using Healthcare.SharedKernel;
using Xunit;

namespace Healthcare.Tests.Fulfillment;

internal sealed class FakeInventoryReader : IInventoryReader
{
    private readonly Dictionary<Guid, StockLevel> _stock = new();
    public void Add(Guid supplierId, Guid skuId, int available) => _stock[supplierId] = new StockLevel(skuId, supplierId, available, 0, 1);
    public Task<StockLevel?> GetAsync(Guid skuId, Guid supplierId, CancellationToken ct) => Task.FromResult<StockLevel?>(_stock.TryGetValue(supplierId, out var level) ? level : null);
    public Task<IReadOnlyCollection<StockLevel>> GetForSkuAsync(Guid skuId, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<StockLevel>>(_stock.Values.Where(x => x.SkuId == skuId).ToList());
}

internal sealed class FakeGeography : IGeographyReader
{
    public Task<ServiceEligibility> EvaluateAsync(Guid supplierId, GeoPoint location, CancellationToken ct) => Task.FromResult(ServiceEligibility.InArea(Guid.NewGuid()));
    public Task<IReadOnlyCollection<ServiceAreaSummary>> GetAreasAsync(Guid supplierId, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<ServiceAreaSummary>>(Array.Empty<ServiceAreaSummary>());
}

internal sealed class FakeCatalog : IProductCatalog
{
    public Task<SkuSummary?> FindSkuByCodeAsync(string code, CancellationToken ct) => Task.FromResult<SkuSummary?>(null);
    public Task<ProductSummary?> FindProductAsync(Guid productId, CancellationToken ct) => Task.FromResult<ProductSummary?>(null);
    public Task<IReadOnlyCollection<ProductSummary>> ListProductsAsync(Guid? categoryId, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<ProductSummary>>(Array.Empty<ProductSummary>());
    public Task<IReadOnlyCollection<SupplierListing>> GetListingsAsync(Guid skuId, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<SupplierListing>>(new[] { new SupplierListing(Guid.NewGuid(), Guid.NewGuid(), "SUP-1", 1000, "IRR", true) });
    public Task<bool> RequiresPrescriptionAsync(Guid productId, CancellationToken ct) => Task.FromResult(false);
}

internal sealed class FakeInventoryWriter : IInventoryService
{
    public List<ReserveStockCommand> Reserved { get; } = new();
    public Task<Result<Guid>> ReserveAsync(ReserveStockCommand command, CancellationToken ct) { Reserved.Add(command); return Task.FromResult(Result<Guid>.Success(Guid.NewGuid())); }
    public Task<Result> ReleaseAsync(ReleaseStockCommand command, CancellationToken ct) => Task.FromResult(Result.Success());
    public Task<Result> DeductAsync(DeductStockCommand command, CancellationToken ct) => Task.FromResult(Result.Success());
    public Task<Result> AdjustAsync(AdjustStockCommand command, CancellationToken ct) => Task.FromResult(Result.Success());
    public Task<int> ReleaseExpiredAsync(CancellationToken ct) => Task.FromResult(0);
}

public sealed class FulfillmentServiceTests
{
    private readonly Guid _skuId = Guid.NewGuid();
    private readonly FakeInventoryReader _reader = new();
    private readonly FakeInventoryWriter _writer = new();
    private readonly FulfillmentService _service;

    public FulfillmentServiceTests() => _service = new FulfillmentService(new FakeCatalog(), _reader, new FakeGeography(), _writer);

    [Fact]
    public async Task RankSuppliers_returns_candidates_ordered_by_stock()
    {
        var low = Guid.NewGuid(); var high = Guid.NewGuid();
        _reader.Add(low, _skuId, 2); _reader.Add(high, _skuId, 10);
        var result = await _service.RankSuppliersAsync(_skuId, new GeoPoint(35.6, 51.3), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value.First().SupplierId.Should().Be(high);
        result.Value.First().AvailableQuantity.Should().Be(10);
    }

    [Fact]
    public async Task Reserve_when_stock_is_sufficient()
    {
        var supplier = Guid.NewGuid(); _reader.Add(supplier, _skuId, 5);
        var result = await _service.ReserveAsync(Guid.NewGuid(), _skuId, supplier, 2, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        _writer.Reserved.Should().ContainSingle().Which.Quantity.Should().Be(2);
    }

    [Fact]
    public async Task Reserve_when_stock_is_insufficient_does_not_write()
    {
        var supplier = Guid.NewGuid(); _reader.Add(supplier, _skuId, 1);
        var result = await _service.ReserveAsync(Guid.NewGuid(), _skuId, supplier, 2, CancellationToken.None);
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("fulfillment_insufficient_stock");
        _writer.Reserved.Should().BeEmpty();
    }
}
