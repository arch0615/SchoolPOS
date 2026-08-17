using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SchoolPOS.Data.Services;
using SchoolPOS.Data.Tests.TestSupport;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Entities;
using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Data.Tests;

public class TreasuryServiceTests
{
    private static readonly Guid Operator = Guid.NewGuid();

    private static TreasuryService NewService(TestDatabase db) => new(db.Context, new TestClock());

    [Fact]
    public async Task Open_close_computes_expected_and_variance_including_cash_sales()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var svc = NewService(db);

        var session = await svc.OpenSessionAsync(school.Id, Operator, openingFloat: 500m);
        await svc.RegisterMovementAsync(session.Id, CashMovementType.Income, 100m, "Aportación", Operator);
        await svc.RegisterMovementAsync(session.Id, CashMovementType.Expense, 30m, "Papelería", Operator);

        // Venta en efectivo ligada a la sesión: cuenta en el esperado.
        db.Context.Sales.Add(new Sale
        {
            SchoolId = school.Id,
            CashierId = Operator,
            Tender = TenderType.Cash,
            CashSessionId = session.Id,
            Status = SaleStatus.Completed,
            Total = 200m,
        });
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        // Esperado = 500 + 100 - 30 + 200 = 770. Contado 765 -> variación -5.
        var closed = await svc.CloseSessionAsync(session.Id, countedAmount: 765m);

        closed.Status.Should().Be(CashSessionStatus.Closed);
        closed.ExpectedAmount.Should().Be(770m);
        closed.Variance.Should().Be(-5m);
        closed.ClosedAtUtc.Should().NotBeNull();
    }

    /// <summary>
    /// La misma comprobación que arriba pero pasando por <see cref="SalesService"/> en vez de armar
    /// la venta a mano. La prueba anterior fijaba <c>CashSessionId</c> ella misma, así que probaba
    /// que el arqueo suma bien <b>si</b> la venta viene ligada — no que alguien la ligue. El POS no
    /// lo hacía, y todo el efectivo del día salía como sobrante inexplicado al cerrar la caja.
    /// </summary>
    [Fact]
    public async Task Cash_sale_registered_through_the_sales_service_reaches_the_till_count()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool(taxRate: 0m);
        var product = db.SeedProduct(school.Id, price: 25m, stock: 10m);
        var clock = new TestClock();
        var treasury = new TreasuryService(db.Context, clock);
        var sales = new SalesService(
            db.Context, new InventoryService(db.Context, clock), new BalanceService(db.Context, clock), clock);

        var session = await treasury.OpenSessionAsync(school.Id, Operator, openingFloat: 100m);

        var sale = await sales.RegisterSaleAsync(new SaleRequest(
            school.Id, Operator, TenderType.Cash,
            new[] { new SaleLineRequest(product.Id, "Producto", Quantity: 2m, UnitPrice: 25m) },
            CashSessionId: session.Id));

        sale.CashSessionId.Should().Be(session.Id, "la venta debe quedar ligada a la caja abierta");

        // Esperado = fondo 100 + venta 50 = 150. Contado exacto -> sin variación.
        var closed = await treasury.CloseSessionAsync(session.Id, countedAmount: 150m);
        closed.ExpectedAmount.Should().Be(150m);
        closed.Variance.Should().Be(0m, "el efectivo de la venta ya está contemplado, no es un sobrante");
    }

    /// <summary>
    /// Una venta contra saldo no mueve el cajón: no debe entrar al arqueo aunque ocurra durante la
    /// sesión. El POS le pasa CashSessionId solo a las de efectivo.
    /// </summary>
    [Fact]
    public async Task Balance_sale_does_not_affect_the_till_count()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool(taxRate: 0m);
        var account = db.SeedStudentAccount(school.Id, balance: 100m);
        var product = db.SeedProduct(school.Id, price: 25m, stock: 10m);
        var clock = new TestClock();
        var treasury = new TreasuryService(db.Context, clock);
        var sales = new SalesService(
            db.Context, new InventoryService(db.Context, clock), new BalanceService(db.Context, clock), clock);

        var session = await treasury.OpenSessionAsync(school.Id, Operator, openingFloat: 100m);
        await sales.RegisterSaleAsync(new SaleRequest(
            school.Id, Operator, TenderType.Balance,
            new[] { new SaleLineRequest(product.Id, "Producto", 2m, 25m) },
            AccountId: account.Id));

        var closed = await treasury.CloseSessionAsync(session.Id, countedAmount: 100m);
        closed.ExpectedAmount.Should().Be(100m, "solo el fondo: el saldo no pasa por el cajón");
        closed.Variance.Should().Be(0m);
    }

    [Fact]
    public async Task Cannot_open_two_sessions_for_same_operator()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var svc = NewService(db);

        await svc.OpenSessionAsync(school.Id, Operator, 100m);
        var act = () => svc.OpenSessionAsync(school.Id, Operator, 100m);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Cannot_register_movement_on_closed_session()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var svc = NewService(db);

        var session = await svc.OpenSessionAsync(school.Id, Operator, 100m);
        await svc.CloseSessionAsync(session.Id, 100m);

        var act = () => svc.RegisterMovementAsync(session.Id, CashMovementType.Income, 10m, "Tarde", Operator);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
