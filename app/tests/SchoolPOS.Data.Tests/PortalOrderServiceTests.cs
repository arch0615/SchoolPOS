using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SchoolPOS.Data.Security;
using SchoolPOS.Data.Services;
using SchoolPOS.Data.Tests.TestSupport;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Entities;
using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Data.Tests;

public class PortalOrderServiceTests
{
    private static PortalOrderService NewService(TestDatabase db, TestClock? clock = null) =>
        new(db.Context, new BalanceService(db.Context, clock ?? new TestClock()), clock ?? new TestClock());

    private static Product SeedMenuProduct(
        TestDatabase db, Guid schoolId, string name, decimal price, DayOfWeek? day = null, bool showInPortal = true,
        decimal stock = 100m)
    {
        var product = new Product
        {
            SchoolId = schoolId, Name = name, Price = price, ShowInPortal = showInPortal, MenuDayOfWeek = day,
            IsActive = true, StockOnHand = stock,
        };
        db.Context.Products.Add(product);
        db.Context.SaveChanges();
        db.Context.ChangeTracker.Clear();
        return product;
    }

    private static async Task<(Guardian Guardian, Account Account)> SeedLinkedGuardianAsync(TestDatabase db, Guid schoolId, decimal balance = 500m)
    {
        var account = db.SeedStudentAccount(schoolId, balance: balance, enrollmentNo: "MAT-1");
        var guardians = new GuardianService(db.Context, new Pbkdf2PasswordHasher(), new TestClock());
        var guardian = await guardians.RegisterAsync(schoolId, "p@c.com", "clave123", "P");
        await guardians.LinkStudentByEnrollmentAsync(guardian.Id, schoolId, "MAT-1");
        return (guardian, account);
    }

    [Fact]
    public async Task Get_catalog_only_returns_portal_visible_or_menu_products()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        SeedMenuProduct(db, school.Id, "Torta", 45m, showInPortal: true);
        SeedMenuProduct(db, school.Id, "Menú del jueves", 55m, day: DayOfWeek.Thursday, showInPortal: false);
        SeedMenuProduct(db, school.Id, "Solo caja", 10m, showInPortal: false); // ni portal ni menú
        var svc = NewService(db);

        var catalog = await svc.GetCatalogAsync(school.Id);

        catalog.Should().HaveCount(2);
        catalog.Should().Contain(c => c.Name == "Torta");
        catalog.Should().Contain(c => c.Name == "Menú del jueves" && c.MenuDayOfWeek == DayOfWeek.Thursday);
        catalog.Should().NotContain(c => c.Name == "Solo caja");
    }

    [Fact]
    public async Task Place_order_debits_balance_immediately_and_records_lines()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var product = SeedMenuProduct(db, school.Id, "Torta", 45m);
        var (guardian, account) = await SeedLinkedGuardianAsync(db, school.Id, balance: 100m);
        var svc = NewService(db);

        var order = await svc.PlaceOrderAsync(
            guardian.Id, account.Id, new[] { new PortalOrderLineRequest(product.Id, 2) }, requestedForDate: null);

        order.Total.Should().Be(90m);
        var balance = await db.NewContext().Accounts.Where(a => a.Id == account.Id).Select(a => a.Balance).SingleAsync();
        balance.Should().Be(10m);

        var rows = await svc.GetOrdersForGuardianAsync(guardian.Id);
        rows.Should().ContainSingle();
        rows[0].Lines.Should().ContainSingle(l => l.Description == "Torta" && l.Quantity == 2);
    }

    [Fact]
    public async Task Place_order_fails_for_an_account_not_owned_by_the_guardian()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var product = SeedMenuProduct(db, school.Id, "Torta", 45m);
        var otherAccount = db.SeedStudentAccount(school.Id, balance: 500m, enrollmentNo: "MAT-OTHER");
        var (guardian, _) = await SeedLinkedGuardianAsync(db, school.Id);
        var svc = NewService(db);

        var act = () => svc.PlaceOrderAsync(
            guardian.Id, otherAccount.Id, new[] { new PortalOrderLineRequest(product.Id, 1) }, null);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Place_order_respects_the_guardians_daily_spend_limit()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var product = SeedMenuProduct(db, school.Id, "Torta", 45m);
        var (guardian, account) = await SeedLinkedGuardianAsync(db, school.Id, balance: 500m);
        await db.Context.Accounts.Where(a => a.Id == account.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.DailySpendLimit, 40m));
        db.Context.ChangeTracker.Clear();
        var svc = NewService(db);

        var act = () => svc.PlaceOrderAsync(
            guardian.Id, account.Id, new[] { new PortalOrderLineRequest(product.Id, 1) }, null); // 45 > 40

        await act.Should().ThrowAsync<Domain.Exceptions.DailyLimitExceededException>();
    }

    /// <summary>
    /// La existencia que ve el portal viene de la última sincronización de la caja (no en tiempo
    /// real) — igual sirve para rechazar lo claramente imposible, en vez de cobrar un pedido que
    /// después habría que devolver porque nunca hubo el artículo.
    /// </summary>
    [Fact]
    public async Task Place_order_rejects_when_synced_stock_is_insufficient()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var product = SeedMenuProduct(db, school.Id, "Torta", 45m, stock: 1m);
        var (guardian, account) = await SeedLinkedGuardianAsync(db, school.Id, balance: 500m);
        var svc = NewService(db);

        var act = () => svc.PlaceOrderAsync(
            guardian.Id, account.Id, new[] { new PortalOrderLineRequest(product.Id, 2) }, null);

        await act.Should().ThrowAsync<Domain.Exceptions.InsufficientStockException>();

        // No debe cobrarse nada si el pedido se rechaza por falta de existencia.
        var balance = await db.NewContext().Accounts.Where(a => a.Id == account.Id).Select(a => a.Balance).SingleAsync();
        balance.Should().Be(500m);
    }

    [Fact]
    public async Task Cancel_order_refunds_and_marks_cancelled()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var product = SeedMenuProduct(db, school.Id, "Torta", 45m);
        var (guardian, account) = await SeedLinkedGuardianAsync(db, school.Id, balance: 100m);
        var svc = NewService(db);
        var order = await svc.PlaceOrderAsync(
            guardian.Id, account.Id, new[] { new PortalOrderLineRequest(product.Id, 1) }, null);

        await svc.CancelOrderAsync(guardian.Id, order.Id);

        var balance = await db.NewContext().Accounts.Where(a => a.Id == account.Id).Select(a => a.Balance).SingleAsync();
        balance.Should().Be(100m, "se reintegra el cargo completo");
        var status = await db.NewContext().PortalOrders.Where(o => o.Id == order.Id).Select(o => o.Status).SingleAsync();
        status.Should().Be(PortalOrderStatus.Cancelled);
    }

    [Fact]
    public async Task Cannot_cancel_an_order_the_till_already_applied()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var product = SeedMenuProduct(db, school.Id, "Torta", 45m);
        var (guardian, account) = await SeedLinkedGuardianAsync(db, school.Id, balance: 100m);
        var svc = NewService(db);
        var order = await svc.PlaceOrderAsync(
            guardian.Id, account.Id, new[] { new PortalOrderLineRequest(product.Id, 1) }, null);

        await db.Context.PortalOrders.Where(o => o.Id == order.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.AppliedLocally, true));
        db.Context.ChangeTracker.Clear();

        var act = () => svc.CancelOrderAsync(guardian.Id, order.Id);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
