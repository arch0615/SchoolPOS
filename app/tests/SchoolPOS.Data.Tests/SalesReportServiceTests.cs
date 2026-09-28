using FluentAssertions;
using SchoolPOS.Data.Services;
using SchoolPOS.Data.Tests.TestSupport;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Data.Tests;

public class SalesReportServiceTests
{
    private static SalesService NewSales(TestDatabase db)
    {
        var clock = new TestClock();
        return new SalesService(
            db.Context, new InventoryService(db.Context, clock), new BalanceService(db.Context, clock),
            new TreasuryService(db.Context, clock), clock);
    }

    /// <summary>
    /// El desglose por forma de cobro (FR-SAL-6) es justo lo que la escuela pidió para poder sacar
    /// reportes de ventas por tarjeta/otro, no solo saldo/efectivo.
    /// </summary>
    [Fact]
    public async Task Summary_breaks_down_totals_by_every_tender_type()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool(taxRate: 0m);
        var account = db.SeedStudentAccount(school.Id, balance: 100m);
        var product = db.SeedProduct(school.Id, price: 10m, stock: 100m);
        var sales = NewSales(db);

        await sales.RegisterSaleAsync(new SaleRequest(
            school.Id, Guid.NewGuid(), TenderType.Balance,
            new[] { new SaleLineRequest(product.Id, "P", 1m, 10m) }, AccountId: account.Id));
        await sales.RegisterSaleAsync(new SaleRequest(
            school.Id, Guid.NewGuid(), TenderType.Cash,
            new[] { new SaleLineRequest(product.Id, "P", 1m, 10m) }, AmountTendered: 10m));
        await sales.RegisterSaleAsync(new SaleRequest(
            school.Id, Guid.NewGuid(), TenderType.CreditCard,
            new[] { new SaleLineRequest(product.Id, "P", 2m, 10m) }));
        await sales.RegisterSaleAsync(new SaleRequest(
            school.Id, Guid.NewGuid(), TenderType.Other,
            new[] { new SaleLineRequest(product.Id, "P", 3m, 10m) }));

        var report = new SalesReportService(db.NewContext());
        var summary = await report.GetSummaryAsync(school.Id, null, null);

        summary.SaleCount.Should().Be(4);
        summary.Total.Should().Be(70m);
        summary.TotalByBalance.Should().Be(10m);
        summary.TotalByCash.Should().Be(10m);
        summary.TotalByCreditCard.Should().Be(20m);
        summary.TotalByOther.Should().Be(30m);
    }
}
