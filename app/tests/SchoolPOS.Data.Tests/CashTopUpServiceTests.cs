using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SchoolPOS.Data.Services;
using SchoolPOS.Data.Sync;
using SchoolPOS.Data.Tests.TestSupport;
using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Data.Tests;

/// <summary>
/// Recarga en efectivo del mostrador. Es lo que cierra el modelo prepagado en la instalación de
/// una sola caja: sin portal ni pasarela, un alumno recién inscrito se quedaba en $0.00.
/// </summary>
public class CashTopUpServiceTests
{
    private static readonly Guid Operator = Guid.NewGuid();

    private sealed record Ctx(CashTopUpService Cash, TreasuryService Treasury, TestClock Clock);

    private static Ctx New(TestDatabase db)
    {
        var clock = new TestClock();
        var treasury = new TreasuryService(db.Context, clock);
        var balance = new BalanceService(db.Context, clock);
        return new Ctx(new CashTopUpService(db.Context, balance, treasury, clock), treasury, clock);
    }

    [Fact]
    public async Task Cash_top_up_credits_the_student_and_lands_in_the_till()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool(commissionRate: 0.05m);
        var account = db.SeedStudentAccount(school.Id, balance: 0m);
        var ctx = New(db);
        var session = await ctx.Treasury.OpenSessionAsync(school.Id, Operator, openingFloat: 100m);

        await ctx.Cash.CreateAsync(school.Id, account.Id, 150m, Operator, session.Id);

        var read = db.NewContext();
        (await read.Accounts.Where(a => a.Id == account.Id).Select(a => a.Balance).SingleAsync())
            .Should().Be(150m);
        (await read.BalanceMovements.CountAsync(m => m.Type == MovementType.TopUp)).Should().Be(1);

        // El efectivo entró al cajón: fondo 100 + recarga 150 = 250 esperado.
        var closed = await ctx.Treasury.CloseSessionAsync(session.Id, countedAmount: 250m);
        closed.ExpectedAmount.Should().Be(250m);
        closed.Variance.Should().Be(0m);
    }

    /// <summary>
    /// El proveedor no procesa este dinero, así que no hay nada que separar por split: la recarga
    /// se guarda sin comisión y no debe aparecer en los reportes del proveedor.
    /// </summary>
    [Fact]
    public async Task Cash_top_up_carries_no_commission_and_stays_out_of_vendor_reports()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool(commissionRate: 0.05m);
        var account = db.SeedStudentAccount(school.Id, balance: 0m);
        var ctx = New(db);
        var session = await ctx.Treasury.OpenSessionAsync(school.Id, Operator, 0m);

        var topUp = await ctx.Cash.CreateAsync(school.Id, account.Id, 200m, Operator, session.Id);

        topUp.Origin.Should().Be(TopUpOrigin.Cash);
        topUp.CommissionRate.Should().Be(0m);
        topUp.CommissionAmount.Should().Be(0m);

        var reports = new CommissionReportService(db.NewContext());
        var rollup = await reports.GetVendorRollupAsync(null, null);
        rollup.TotalRecharged.Should().Be(0m, "el efectivo del mostrador no es ingreso procesado por el proveedor");
        rollup.TotalCommission.Should().Be(0m);
    }

    [Fact]
    public async Task Cash_top_up_without_an_open_till_is_refused_and_credits_nothing()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var account = db.SeedStudentAccount(school.Id, balance: 0m);
        var ctx = New(db);

        var act = () => ctx.Cash.CreateAsync(school.Id, account.Id, 100m, Operator, Guid.Empty);
        await act.Should().ThrowAsync<InvalidOperationException>();

        var read = db.NewContext();
        (await read.Accounts.Where(a => a.Id == account.Id).Select(a => a.Balance).SingleAsync())
            .Should().Be(0m);
        (await read.TopUps.CountAsync()).Should().Be(0);
    }

    /// <summary>
    /// La recarga de efectivo nace en la escuela, así que tiene que subir al portal o el tutor
    /// vería un saldo distinto al real. Las de pasarela no: esas ya las asentó el portal al
    /// confirmarlas, y subir el asiento local las duplicaría.
    /// </summary>
    [Fact]
    public async Task Cash_top_ups_sync_up_but_gateway_ones_do_not()
    {
        var schoolId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        using var cloud = new TestDatabase();
        using var local = new TestDatabase();
        cloud.SeedRoster(schoolId, studentId, accountId);
        local.SeedRoster(schoolId, studentId, accountId);

        // Una recarga por pasarela que baja del portal y se aplica en la escuela.
        cloud.SeedConfirmedTopUp(schoolId, accountId, 100m, "MP-1");
        var clock = new TestClock();
        var agent = new SyncAgent(cloud.Context, local.Context, new BalanceService(local.Context, clock), clock);
        await agent.PullTopUpsAsync();

        // Y una en efectivo capturada en el mostrador.
        var treasury = new TreasuryService(local.Context, clock);
        var session = await treasury.OpenSessionAsync(schoolId, Operator, 0m);
        var cash = new CashTopUpService(
            local.Context, new BalanceService(local.Context, clock), treasury, clock);
        await cash.CreateAsync(schoolId, accountId, 50m, Operator, session.Id);

        var report = await agent.PushConsumptionAsync();

        report.Pushed.Should().Be(1, "solo la de efectivo sube");
        var cloudTopUpMovements = await cloud.NewContext().BalanceMovements
            .Where(m => m.Type == MovementType.TopUp).ToListAsync();
        cloudTopUpMovements.Should().ContainSingle();
        cloudTopUpMovements[0].Amount.Should().Be(50m);

        // Nada queda en la cola: la de pasarela se marca aunque no suba.
        (await local.NewContext().BalanceMovements.CountAsync(m => m.SyncedToCloudAtUtc == null))
            .Should().Be(0);
    }
}
