using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SchoolPOS.Data.Services;
using SchoolPOS.Data.Tests.TestSupport;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Enums;
using SchoolPOS.Domain.Exceptions;

namespace SchoolPOS.Data.Tests;

public class SalesServiceTests
{
    private sealed record Services(SalesService Sales, TestClock Clock);

    private static Services NewServices(TestDatabase db)
    {
        var clock = new TestClock();
        var inventory = new InventoryService(db.Context, clock);
        var balance = new BalanceService(db.Context, clock);
        return new Services(new SalesService(db.Context, inventory, balance, clock), clock);
    }

    [Fact]
    public async Task Balance_sale_decrements_stock_and_debits_balance_atomically()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool(taxRate: 0m);
        var account = db.SeedStudentAccount(school.Id, balance: 100m);
        var product = db.SeedProduct(school.Id, price: 10m, stock: 10m);
        var svc = NewServices(db).Sales;

        var request = new SaleRequest(
            school.Id, CashierId: Guid.NewGuid(), TenderType.Balance,
            Lines: new[] { new SaleLineRequest(product.Id, "Producto", Quantity: 2m, UnitPrice: 10m) },
            AccountId: account.Id);

        var sale = await svc.RegisterSaleAsync(request);

        sale.Total.Should().Be(20m);
        var ctx = db.NewContext();
        (await ctx.Products.Where(p => p.Id == product.Id).Select(p => p.StockOnHand).SingleAsync()).Should().Be(8m);
        (await ctx.Accounts.Where(a => a.Id == account.Id).Select(a => a.Balance).SingleAsync()).Should().Be(80m);
        (await ctx.Sales.CountAsync(s => s.Id == sale.Id)).Should().Be(1);
        (await ctx.BalanceMovements.CountAsync(m => m.AccountId == account.Id && m.Type == MovementType.Sale)).Should().Be(1);
    }

    [Fact]
    public async Task Tax_added_when_school_configured_exclusive()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool(taxRate: 0.16m, taxInclusive: false);
        var account = db.SeedStudentAccount(school.Id, balance: 100m);
        var product = db.SeedProduct(school.Id, price: 10m, stock: 10m);
        var svc = NewServices(db).Sales;

        var request = new SaleRequest(
            school.Id, Guid.NewGuid(), TenderType.Balance,
            new[] { new SaleLineRequest(product.Id, "Producto", 2m, 10m) },
            AccountId: account.Id);

        var sale = await svc.RegisterSaleAsync(request);

        sale.Subtotal.Should().Be(20m);
        sale.TaxTotal.Should().Be(3.20m);
        sale.Total.Should().Be(23.20m);
        (await db.NewContext().Accounts.Where(a => a.Id == account.Id).Select(a => a.Balance).SingleAsync())
            .Should().Be(76.80m);
    }

    /// <summary>Atomicidad: si un renglón no tiene stock, se revierte TODA la venta.</summary>
    [Fact]
    public async Task Sale_rolls_back_entirely_when_a_line_lacks_stock()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var account = db.SeedStudentAccount(school.Id, balance: 100m);
        var ok = db.SeedProduct(school.Id, price: 10m, stock: 10m, name: "OK");
        var low = db.SeedProduct(school.Id, price: 10m, stock: 1m, name: "Bajo");
        var svc = NewServices(db).Sales;

        var request = new SaleRequest(
            school.Id, Guid.NewGuid(), TenderType.Balance,
            new[]
            {
                new SaleLineRequest(ok.Id, "OK", 2m, 10m),
                new SaleLineRequest(low.Id, "Bajo", 5m, 10m), // excede stock
            },
            AccountId: account.Id);

        var act = () => svc.RegisterSaleAsync(request);
        await act.Should().ThrowAsync<InsufficientStockException>();

        var ctx = db.NewContext();
        (await ctx.Sales.CountAsync()).Should().Be(0, "no debe quedar venta");
        (await ctx.Products.Where(p => p.Id == ok.Id).Select(p => p.StockOnHand).SingleAsync())
            .Should().Be(10m, "el stock del primer renglón se revierte");
        (await ctx.Accounts.Where(a => a.Id == account.Id).Select(a => a.Balance).SingleAsync())
            .Should().Be(100m, "no se debita saldo");
        (await ctx.StockMovements.CountAsync()).Should().Be(0);
    }

    /// <summary>Atomicidad: si el saldo no alcanza, se revierte el stock ya descontado.</summary>
    [Fact]
    public async Task Sale_rolls_back_stock_when_balance_insufficient()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var account = db.SeedStudentAccount(school.Id, balance: 5m);
        var product = db.SeedProduct(school.Id, price: 10m, stock: 10m);
        var svc = NewServices(db).Sales;

        var request = new SaleRequest(
            school.Id, Guid.NewGuid(), TenderType.Balance,
            new[] { new SaleLineRequest(product.Id, "Producto", 2m, 10m) },
            AccountId: account.Id);

        var act = () => svc.RegisterSaleAsync(request);
        await act.Should().ThrowAsync<InsufficientBalanceException>();

        var ctx = db.NewContext();
        (await ctx.Products.Where(p => p.Id == product.Id).Select(p => p.StockOnHand).SingleAsync())
            .Should().Be(10m, "el stock descontado se revierte");
        (await ctx.Sales.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Partial_refund_restores_stock_and_balance_and_marks_status()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var account = db.SeedStudentAccount(school.Id, balance: 100m);
        var product = db.SeedProduct(school.Id, price: 10m, stock: 10m);
        var svc = NewServices(db).Sales;

        var sale = await svc.RegisterSaleAsync(new SaleRequest(
            school.Id, Guid.NewGuid(), TenderType.Balance,
            new[] { new SaleLineRequest(product.Id, "Producto", 2m, 10m) },
            AccountId: account.Id));

        var lineId = (await db.NewContext().SaleLines.SingleAsync(l => l.SaleId == sale.Id)).Id;

        var refunded = await svc.RefundSaleAsync(
            sale.Id, new[] { (lineId, 1m) }, operatorId: Guid.NewGuid());

        refunded.Status.Should().Be(SaleStatus.PartiallyRefunded);
        var ctx = db.NewContext();
        (await ctx.Products.Where(p => p.Id == product.Id).Select(p => p.StockOnHand).SingleAsync())
            .Should().Be(9m, "1 unidad reingresa al stock");
        (await ctx.Accounts.Where(a => a.Id == account.Id).Select(a => a.Balance).SingleAsync())
            .Should().Be(90m, "se reintegran 10 al saldo");
        (await ctx.SaleLines.Where(l => l.Id == lineId).Select(l => l.QuantityRefunded).SingleAsync())
            .Should().Be(1m);
    }
}
