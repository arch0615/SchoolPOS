using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SchoolPOS.Data.Services;
using SchoolPOS.Data.Sync;
using SchoolPOS.Data.Tests.TestSupport;
using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Data.Tests;

public class SyncAgentTests
{
    private static readonly Guid SchoolId = Guid.NewGuid();
    private static readonly Guid StudentId = Guid.NewGuid();
    private static readonly Guid AccountId = Guid.NewGuid();

    private static SyncAgent NewAgent(TestDatabase cloud, TestDatabase local)
    {
        var clock = new TestClock();
        var localBalance = new BalanceService(local.Context, clock);
        return new SyncAgent(cloud.Context, local.Context, localBalance, clock);
    }

    [Fact]
    public async Task Pull_applies_confirmed_topup_to_local_ledger_and_acks_cloud()
    {
        using var cloud = new TestDatabase();
        using var local = new TestDatabase();
        cloud.SeedRoster(SchoolId, StudentId, AccountId, balance: 0m);
        local.SeedRoster(SchoolId, StudentId, AccountId, balance: 0m); // fuente de verdad
        cloud.SeedConfirmedTopUp(SchoolId, AccountId, 100m, "MP-1");
        var agent = NewAgent(cloud, local);

        var report = await agent.RunOnceAsync();

        report.TopUpsApplied.Should().Be(1);
        report.TopUpsFailed.Should().Be(0);

        // El saldo local (fuente de verdad) se acreditó al 100%.
        (await local.NewContext().Accounts.Where(a => a.Id == AccountId).Select(a => a.Balance).SingleAsync())
            .Should().Be(100m);
        (await local.NewContext().BalanceMovements.CountAsync(m => m.Type == MovementType.TopUp)).Should().Be(1);

        // La nube quedó marcada como aplicada (acuse).
        (await cloud.NewContext().TopUps.Where(t => t.GatewayRef == "MP-1").Select(t => t.Status).SingleAsync())
            .Should().Be(TopUpStatus.Applied);
    }

    [Fact]
    public async Task Pull_is_idempotent_across_runs()
    {
        using var cloud = new TestDatabase();
        using var local = new TestDatabase();
        cloud.SeedRoster(SchoolId, StudentId, AccountId);
        local.SeedRoster(SchoolId, StudentId, AccountId);
        cloud.SeedConfirmedTopUp(SchoolId, AccountId, 100m, "MP-DUP");
        var agent = NewAgent(cloud, local);

        await agent.RunOnceAsync();
        var second = await agent.RunOnceAsync(); // nada nuevo que aplicar

        second.TopUpsPulled.Should().Be(0);
        (await local.NewContext().Accounts.Where(a => a.Id == AccountId).Select(a => a.Balance).SingleAsync())
            .Should().Be(100m, "solo se acredita una vez");
        (await local.NewContext().BalanceMovements.CountAsync(m => m.Type == MovementType.TopUp)).Should().Be(1);
    }

    [Fact]
    public async Task Offline_local_leaves_topup_pending_then_applies_on_reconnect()
    {
        using var cloud = new TestDatabase();
        cloud.SeedRoster(SchoolId, StudentId, AccountId);
        cloud.SeedConfirmedTopUp(SchoolId, AccountId, 100m, "MP-OFF");

        // 1) Escuela offline: DB local SIN el roster -> el apply falla, no se acusa en la nube.
        using (var localBroken = new TestDatabase())
        {
            var agentOffline = NewAgent(cloud, localBroken);
            var offline = await agentOffline.PullTopUpsAsync();
            offline.Failed.Should().Be(1);
            offline.Applied.Should().Be(0);
        }
        (await cloud.NewContext().TopUps.Where(t => t.GatewayRef == "MP-OFF").Select(t => t.Status).SingleAsync())
            .Should().Be(TopUpStatus.Confirmed, "sigue pendiente hasta reconectar");

        // 2) Reconecta: DB local con roster -> se aplica.
        using var local = new TestDatabase();
        local.SeedRoster(SchoolId, StudentId, AccountId);
        var agent = NewAgent(cloud, local);
        var report = await agent.PullTopUpsAsync();

        report.Applied.Should().Be(1);
        (await local.NewContext().Accounts.Where(a => a.Id == AccountId).Select(a => a.Balance).SingleAsync())
            .Should().Be(100m);
    }

    [Fact]
    public async Task Push_uploads_local_consumption_to_cloud_for_parent_view()
    {
        using var cloud = new TestDatabase();
        using var local = new TestDatabase();
        cloud.SeedRoster(SchoolId, StudentId, AccountId, balance: 100m);
        local.SeedRoster(SchoolId, StudentId, AccountId, balance: 100m);
        // Consumo local: una venta contra saldo.
        var localBalance = new BalanceService(local.Context, new TestClock());
        await localBalance.ChargeSaleAsync(AccountId, 30m, "VENTA-1", Guid.NewGuid());

        var agent = NewAgent(cloud, local);
        var pushed = await agent.PushConsumptionAsync();
        var pushedAgain = await agent.PushConsumptionAsync(); // idempotente

        pushed.Should().Be(1);
        pushedAgain.Should().Be(0);
        var cloudMovements = await cloud.NewContext().BalanceMovements
            .Where(m => m.AccountId == AccountId && m.Type == MovementType.Sale).ToListAsync();
        cloudMovements.Should().ContainSingle();
        cloudMovements[0].Amount.Should().Be(-30m);
    }
}
