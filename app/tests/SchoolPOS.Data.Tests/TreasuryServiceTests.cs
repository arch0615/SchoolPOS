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
