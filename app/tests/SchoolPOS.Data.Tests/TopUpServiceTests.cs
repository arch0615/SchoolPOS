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

    [Fact]
    public async Task Full_flow_create_confirm_apply_credits_full_amount()
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
            .Should().Be(100m, "el estudiante recibe el 100%");
        (await ctx.TopUps.Where(t => t.Id == created.TopUp.Id).Select(t => t.Status).SingleAsync())
            .Should().Be(TopUpStatus.Applied);
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
