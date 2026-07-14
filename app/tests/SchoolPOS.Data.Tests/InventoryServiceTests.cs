using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SchoolPOS.Data.Services;
using SchoolPOS.Data.Tests.TestSupport;
using SchoolPOS.Domain.Enums;
using SchoolPOS.Domain.Exceptions;

namespace SchoolPOS.Data.Tests;

public class InventoryServiceTests
{
    private static readonly Guid Operator = Guid.NewGuid();

    private static InventoryService NewService(TestDatabase db) => new(db.Context, new TestClock());

    [Fact]
    public async Task Entry_then_exit_reconciles_kardex_with_stock()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var product = db.SeedProduct(school.Id, stock: 0m);
        var svc = NewService(db);

        await svc.RegisterEntryAsync(product.Id, 10m, unitCost: 12m, "OC-1", Operator);
        await svc.RegisterExitAsync(product.Id, 3m, "Merma", "AJ-1", Operator);

        var ctx = db.NewContext();
        var stock = await ctx.Products.Where(p => p.Id == product.Id).Select(p => p.StockOnHand).SingleAsync();
        var kardex = await ctx.StockMovements.Where(m => m.ProductId == product.Id).Select(m => m.Quantity).ToListAsync();

        stock.Should().Be(7m);
        kardex.Sum().Should().Be(stock);
    }

    [Fact]
    public async Task Exit_beyond_stock_is_rejected_and_nothing_changes()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var product = db.SeedProduct(school.Id, stock: 2m);
        var svc = NewService(db);

        var act = () => svc.RegisterExitAsync(product.Id, 5m, "Venta", "V-1", Operator);

        await act.Should().ThrowAsync<InsufficientStockException>();
        var ctx = db.NewContext();
        (await ctx.Products.Where(p => p.Id == product.Id).Select(p => p.StockOnHand).SingleAsync()).Should().Be(2m);
        (await ctx.StockMovements.CountAsync(m => m.ProductId == product.Id)).Should().Be(0);
    }

    [Fact]
    public async Task AdjustToCount_sets_stock_and_records_signed_delta()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var product = db.SeedProduct(school.Id, stock: 10m);
        var svc = NewService(db);

        await svc.AdjustToCountAsync(product.Id, countedQuantity: 8m, "Conteo físico", Operator);

        var ctx = db.NewContext();
        (await ctx.Products.Where(p => p.Id == product.Id).Select(p => p.StockOnHand).SingleAsync()).Should().Be(8m);
        var mv = await ctx.StockMovements.SingleAsync(m => m.ProductId == product.Id && m.Type == StockMovementType.Adjustment);
        mv.Quantity.Should().Be(-2m);
        mv.StockAfter.Should().Be(8m);
    }

    [Fact]
    public async Task GetLowStock_returns_products_at_or_below_minimum()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        db.SeedProduct(school.Id, stock: 2m, minStock: 5m, name: "Bajo");
        db.SeedProduct(school.Id, stock: 20m, minStock: 5m, name: "Alto");
        var svc = NewService(db);

        var low = await svc.GetLowStockAsync(school.Id);

        low.Should().ContainSingle().Which.Name.Should().Be("Bajo");
    }
}
