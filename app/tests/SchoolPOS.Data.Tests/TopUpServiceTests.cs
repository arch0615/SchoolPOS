using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SchoolPOS.Data.Services;
using SchoolPOS.Data.Tests.TestSupport;
using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Data.Tests;

public class TopUpServiceTests
{
    private sealed record Ctx(TopUpService Service, FakePaymentGateway Gateway);

    private static Ctx NewService(TestDatabase db)
    {
        var clock = new TestClock();
        var gateway = new FakePaymentGateway();
        var balance = new BalanceService(db.Context, clock);
        return new Ctx(new TopUpService(db.Context, gateway, balance, clock), gateway);
    }

    [Fact]
    public async Task Create_computes_commission_and_sends_split_to_gateway()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool(commissionRate: 0.05m);
        var account = db.SeedStudentAccount(school.Id);
        var (svc, gateway) = NewService(db);

        var created = await svc.CreateAsync(school.Id, account.Id, 100m);

        created.TopUp.Amount.Should().Be(100m);            // 100% al estudiante
        created.TopUp.CommissionAmount.Should().Be(5m);    // 5% comisión
        created.TopUp.Status.Should().Be(TopUpStatus.Pending);
        created.CheckoutUrl.Should().StartWith("https://mp.test/checkout/");
        // La comisión viaja como split (application_fee) a la pasarela.
        gateway.LastIntent!.CommissionAmount.Should().Be(5m);
        gateway.LastIntent!.Amount.Should().Be(100m);
    }

    /// <summary>
    /// ApplyConfirmedAsync es lo que llama el webhook de la pasarela (nube): debe acreditar el
    /// saldo de inmediato para que el tutor lo vea, pero SIN marcar la recarga como entregada a
    /// ninguna caja. Antes de esta corrección llamaba a IBalanceService.ApplyTopUpAsync — pensado
    /// solo para el Sync Agent sobre la DB local — y dejaba AppliedLocally=true desde la nube, por
    /// lo que PullTopUpsAsync nunca volvía a ofrecerle esa recarga a ninguna caja (se reprodujo así
    /// en producción: toda recarga confirmada requería "Reenviar recargas" manual para llegar).
    /// </summary>
    [Fact]
    public async Task Webhook_confirm_credits_balance_without_marking_it_delivered_to_any_till()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool(commissionRate: 0.05m);
        var account = db.SeedStudentAccount(school.Id, balance: 0m);
        var (svc, _) = NewService(db);

        var created = await svc.CreateAsync(school.Id, account.Id, 100m);
        await svc.ConfirmAsync(created.TopUp.GatewayRef);
        await svc.ApplyConfirmedAsync(created.TopUp.Id);

        var ctx = db.NewContext();
        (await ctx.Accounts.Where(a => a.Id == account.Id).Select(a => a.Balance).SingleAsync())
            .Should().Be(100m, "el tutor debe ver su saldo de inmediato al confirmarse el pago");
        var topUp = await ctx.TopUps.SingleAsync(t => t.Id == created.TopUp.Id);
        topUp.Status.Should().Be(TopUpStatus.Confirmed, "todavía no la aplicó ninguna caja");
        topUp.AppliedLocally.Should().BeFalse("PullTopUpsAsync debe poder seguir ofreciéndosela a las cajas");
    }


    [Fact]
    public async Task Cannot_apply_before_confirmed()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var account = db.SeedStudentAccount(school.Id);
        var (svc, _) = NewService(db);

        var created = await svc.CreateAsync(school.Id, account.Id, 100m);
        var act = () => svc.ApplyConfirmedAsync(created.TopUp.Id); // aún Pendiente

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Confirm_and_apply_are_idempotent()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var account = db.SeedStudentAccount(school.Id, balance: 0m);
        var (svc, _) = NewService(db);

        var created = await svc.CreateAsync(school.Id, account.Id, 100m);
        await svc.ConfirmAsync(created.TopUp.GatewayRef);
        await svc.ConfirmAsync(created.TopUp.GatewayRef); // repetido
        await svc.ApplyConfirmedAsync(created.TopUp.Id);
        await svc.ApplyConfirmedAsync(created.TopUp.Id);  // repetido (webhook duplicado)

        var ctx = db.NewContext();
        (await ctx.Accounts.Where(a => a.Id == account.Id).Select(a => a.Balance).SingleAsync())
            .Should().Be(100m, "solo se acredita una vez pese a webhooks duplicados");
        (await ctx.BalanceMovements.CountAsync(m => m.Type == MovementType.TopUp)).Should().Be(1);
    }
}
