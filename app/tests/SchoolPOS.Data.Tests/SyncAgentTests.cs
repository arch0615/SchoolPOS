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

        pushed.Pushed.Should().Be(1);
        pushedAgain.Pushed.Should().Be(0);
        var cloudMovements = await cloud.NewContext().BalanceMovements
            .Where(m => m.AccountId == AccountId && m.Type == MovementType.Sale).ToListAsync();
        cloudMovements.Should().ContainSingle();
        cloudMovements[0].Amount.Should().Be(-30m);
    }

    /// <summary>
    /// Un asiento subido queda marcado, y la siguiente corrida ya no vuelve a leerlo. Sin esta
    /// marca el agente releía todo el historial de la escuela en cada ciclo, para siempre.
    /// </summary>
    [Fact]
    public async Task Pushed_movements_are_marked_and_not_read_again()
    {
        using var cloud = new TestDatabase();
        using var local = new TestDatabase();
        cloud.SeedRoster(SchoolId, StudentId, AccountId, balance: 100m);
        local.SeedRoster(SchoolId, StudentId, AccountId, balance: 100m);
        var localBalance = new BalanceService(local.Context, new TestClock());
        await localBalance.ChargeSaleAsync(AccountId, 30m, "VENTA-1", Guid.NewGuid());

        var agent = NewAgent(cloud, local);
        await agent.PushConsumptionAsync();

        var pendingAfter = await local.NewContext().BalanceMovements
            .CountAsync(m => m.Type == MovementType.Sale && m.SyncedToCloudAtUtc == null);
        pendingAfter.Should().Be(0, "lo ya subido no vuelve a la cola");

        // Consumo nuevo: solo ese entra en la siguiente corrida.
        await localBalance.ChargeSaleAsync(AccountId, 20m, "VENTA-2", Guid.NewGuid());
        var second = await agent.PushConsumptionAsync();
        second.Pushed.Should().Be(1, "solo el asiento nuevo, no los anteriores");
    }

    /// <summary>
    /// La recarga no es consumo: la origina la nube y baja hacia la escuela. No debe volver a
    /// subir (duplicaría el movimiento en la vista del padre).
    /// </summary>
    [Fact]
    public async Task Push_ignores_topup_movements()
    {
        using var cloud = new TestDatabase();
        using var local = new TestDatabase();
        cloud.SeedRoster(SchoolId, StudentId, AccountId);
        local.SeedRoster(SchoolId, StudentId, AccountId);
        cloud.SeedConfirmedTopUp(SchoolId, AccountId, 100m, "MP-PUSH");

        var agent = NewAgent(cloud, local);
        await agent.PullTopUpsAsync();  // crea un asiento TopUp local
        var pushed = await agent.PushConsumptionAsync();

        pushed.Pushed.Should().Be(0);
        (await cloud.NewContext().BalanceMovements.CountAsync(m => m.Type == MovementType.TopUp))
            .Should().Be(0, "el asiento de la recarga vive en la DB local, no se replica de vuelta");
    }

    /// <summary>
    /// Cuenta que la nube todavía no conoce (roster desfasado): el asiento no se sube, pero
    /// tampoco se marca — tiene que seguir pendiente y subir cuando el roster se ponga al día.
    /// </summary>
    [Fact]
    public async Task Movement_for_unknown_cloud_account_stays_pending()
    {
        using var cloud = new TestDatabase();
        using var local = new TestDatabase();
        local.SeedRoster(SchoolId, StudentId, AccountId, balance: 100m); // la nube NO tiene el roster
        var localBalance = new BalanceService(local.Context, new TestClock());
        await localBalance.ChargeSaleAsync(AccountId, 30m, "VENTA-HUERFANA", Guid.NewGuid());

        var agent = NewAgent(cloud, local);
        var report = await agent.PushConsumptionAsync();

        report.Pushed.Should().Be(0);
        report.Skipped.Should().Be(1);
        (await local.NewContext().BalanceMovements
            .CountAsync(m => m.Type == MovementType.Sale && m.SyncedToCloudAtUtc == null))
            .Should().Be(1, "sigue en la cola para la próxima corrida");

        // La nube recibe el roster: ahora sí sube.
        cloud.SeedRoster(SchoolId, StudentId, AccountId, balance: 100m);
        var second = await agent.PushConsumptionAsync();
        second.Pushed.Should().Be(1);
        second.Skipped.Should().Be(0);
    }

    /// <summary>
    /// El saldo que el portal le muestra al tutor sale de <c>Accounts.Balance</c> en la nube. La
    /// subida asentaba el movimiento pero no tocaba ese saldo: la compra aparecía en la lista y el
    /// saldo seguía igual y de más, contradiciéndose en la misma pantalla.
    /// </summary>
    [Fact]
    public async Task Pushing_consumption_also_lowers_the_balance_the_parent_sees()
    {
        using var cloud = new TestDatabase();
        using var local = new TestDatabase();
        cloud.SeedRoster(SchoolId, StudentId, AccountId, balance: 100m);
        local.SeedRoster(SchoolId, StudentId, AccountId, balance: 100m);

        var localBalance = new BalanceService(local.Context, new TestClock());
        await localBalance.ChargeSaleAsync(AccountId, 30m, "VENTA-1", Guid.NewGuid());

        await NewAgent(cloud, local).PushConsumptionAsync();

        var cloudCtx = cloud.NewContext();
        (await cloudCtx.Accounts.Where(a => a.Id == AccountId).Select(a => a.Balance).SingleAsync())
            .Should().Be(70m, "el tutor debe ver el saldo ya descontado");

        // Y sigue cuadrando con los asientos que la nube tiene.
        var movements = await cloudCtx.BalanceMovements
            .Where(m => m.AccountId == AccountId).Select(m => m.Amount).ToListAsync();
        (100m + movements.Sum()).Should().Be(70m);
    }

    /// <summary>
    /// Se aplica la variación, no el <c>BalanceAfter</c> local. Si la nube ya confirmó una recarga
    /// que la escuela todavía no baja, su saldo va por delante a propósito; copiar el local
    /// borraría dinero que el tutor ya pagó.
    /// </summary>
    [Fact]
    public async Task Cloud_balance_keeps_top_ups_the_school_has_not_pulled_yet()
    {
        using var cloud = new TestDatabase();
        using var local = new TestDatabase();
        // La nube va por delante: 200 contra 100 en la escuela (una recarga sin bajar).
        cloud.SeedRoster(SchoolId, StudentId, AccountId, balance: 200m);
        local.SeedRoster(SchoolId, StudentId, AccountId, balance: 100m);

        var localBalance = new BalanceService(local.Context, new TestClock());
        var movement = await localBalance.ChargeSaleAsync(AccountId, 30m, "VENTA-1", Guid.NewGuid());
        movement.BalanceAfter.Should().Be(70m, "así quedó el saldo en la escuela");

        await NewAgent(cloud, local).PushConsumptionAsync();

        (await cloud.NewContext().Accounts.Where(a => a.Id == AccountId).Select(a => a.Balance).SingleAsync())
            .Should().Be(170m, "200 recibidos menos 30 gastados; copiar el 70 local perdería una recarga");
    }

    /// <summary>Reintentar un lote ya subido no debe volver a descontar.</summary>
    [Fact]
    public async Task Re_pushing_does_not_double_count_the_balance()
    {
        using var cloud = new TestDatabase();
        using var local = new TestDatabase();
        cloud.SeedRoster(SchoolId, StudentId, AccountId, balance: 100m);
        local.SeedRoster(SchoolId, StudentId, AccountId, balance: 100m);

        var localBalance = new BalanceService(local.Context, new TestClock());
        await localBalance.ChargeSaleAsync(AccountId, 30m, "VENTA-1", Guid.NewGuid());

        var agent = NewAgent(cloud, local);
        await agent.PushConsumptionAsync();
        await agent.PushConsumptionAsync();   // idempotente

        (await cloud.NewContext().Accounts.Where(a => a.Id == AccountId).Select(a => a.Balance).SingleAsync())
            .Should().Be(70m);
    }
}
