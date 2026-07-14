using FluentAssertions;
using SchoolPOS.Data.Reporting;
using SchoolPOS.Data.Services;
using SchoolPOS.Data.Tests.TestSupport;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Data.Tests;

public class ReportingServicesTests
{
    private sealed record Ctx(SalesService Sales, InventoryService Inventory, TreasuryService Treasury,
        BalanceService Balance, TestClock Clock);

    private static Ctx NewCtx(TestDatabase db)
    {
        var clock = new TestClock();
        var inv = new InventoryService(db.Context, clock);
        var bal = new BalanceService(db.Context, clock);
        return new Ctx(new SalesService(db.Context, inv, bal, clock), inv, new TreasuryService(db.Context, clock), bal, clock);
    }

    [Fact]
    public async Task Sales_report_summary_and_breakdowns()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var account = db.SeedStudentAccount(school.Id, balance: 1000m);
        var pA = db.SeedProduct(school.Id, price: 10m, stock: 100m, name: "A");
        var pB = db.SeedProduct(school.Id, price: 20m, stock: 100m, name: "B");
        var ctx = NewCtx(db);
        var cashier = Guid.NewGuid();

        // Venta 1: saldo, 2xA + 1xB = 40
        await ctx.Sales.RegisterSaleAsync(new SaleRequest(school.Id, cashier, TenderType.Balance,
            new[] { new SaleLineRequest(pA.Id, "A", 2m, 10m), new SaleLineRequest(pB.Id, "B", 1m, 20m) },
            StudentId: null, AccountId: account.Id));
        // Venta 2: efectivo, 3xA = 30
        await ctx.Sales.RegisterSaleAsync(new SaleRequest(school.Id, cashier, TenderType.Cash,
            new[] { new SaleLineRequest(pA.Id, "A", 3m, 10m) }));

        var reports = new SalesReportService(db.Context);
        var summary = await reports.GetSummaryAsync(school.Id, null, null);
        summary.SaleCount.Should().Be(2);
        summary.Total.Should().Be(70m);
        summary.TotalByBalance.Should().Be(40m);
        summary.TotalByCash.Should().Be(30m);

        var byProduct = await reports.GetByProductAsync(school.Id, null, null);
        byProduct.Single(p => p.Description == "A").Quantity.Should().Be(5m);   // 2 + 3
        byProduct.Single(p => p.Description == "A").Revenue.Should().Be(50m);   // 20 + 30

        var byCashier = await reports.GetByCashierAsync(school.Id, null, null);
        byCashier.Single().Total.Should().Be(70m);
        byCashier.Single().SaleCount.Should().Be(2);
    }

    [Fact]
    public async Task Financial_cash_flow_and_customer_balances()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var account = db.SeedStudentAccount(school.Id, balance: 250m);
        var product = db.SeedProduct(school.Id, price: 10m, stock: 100m);
        var ctx = NewCtx(db);

        var session = await ctx.Treasury.OpenSessionAsync(school.Id, Guid.NewGuid(), 100m);
        await ctx.Treasury.RegisterMovementAsync(session.Id, CashMovementType.Income, 50m, "Aporte", Guid.NewGuid());
        await ctx.Treasury.RegisterMovementAsync(session.Id, CashMovementType.Expense, 20m, "Gasto", Guid.NewGuid());
        // Venta en efectivo ligada a la sesión: 2x10 = 20
        await ctx.Sales.RegisterSaleAsync(new SaleRequest(school.Id, Guid.NewGuid(), TenderType.Cash,
            new[] { new SaleLineRequest(product.Id, "P", 2m, 10m) }, CashSessionId: session.Id));

        var fin = new FinancialReportService(db.Context);
        var flow = await fin.GetCashFlowAsync(school.Id, null, null);
        flow.CashSales.Should().Be(20m);
        flow.ManualIncome.Should().Be(50m);
        flow.ManualExpense.Should().Be(20m);
        flow.Net.Should().Be(50m); // 20 + 50 - 20

        var balances = await fin.GetCustomerBalancesAsync(school.Id);
        balances.AccountCount.Should().Be(1);
        balances.TotalBalance.Should().Be(250m);
    }

    [Fact]
    public async Task Balance_adjustment_is_written_to_audit_and_queryable()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var account = db.SeedStudentAccount(school.Id, balance: 100m);
        var ctx = NewCtx(db);
        var admin = Guid.NewGuid();

        await ctx.Balance.AdjustAsync(account.Id, -25m, "Corrección", admin);

        var audit = new AuditLogQueryService(db.Context);
        var entries = await audit.QueryAsync(school.Id, null, null, action: null);

        entries.Should().ContainSingle(e => e.Action == "BalanceAdjustment");
        var entry = entries.Single(e => e.Action == "BalanceAdjustment");
        entry.Entity.Should().Be("Account");
        entry.Before.Should().Be("100.00");
        entry.After.Should().Contain("75.00");

        var filtered = await audit.QueryAsync(school.Id, null, null, action: "NoExiste");
        filtered.Should().BeEmpty();
    }

    [Fact]
    public void Csv_escapes_special_characters()
    {
        var csv = Csv.Build(
            new[] { "Nombre", "Nota" },
            new[] { new[] { "Ana, López", "Dijo \"hola\"" }, new[] { "Luis", "línea1\nlínea2" } });

        csv.Should().Contain("\"Ana, López\"");
        csv.Should().Contain("\"Dijo \"\"hola\"\"\"");
        csv.Should().Contain("\"línea1\nlínea2\"");
    }
}
