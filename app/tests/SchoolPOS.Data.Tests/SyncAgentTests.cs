using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SchoolPOS.Data.Services;
using SchoolPOS.Data.Sync;
using SchoolPOS.Data.Tests.TestSupport;
using SchoolPOS.Domain.Enums;
using SchoolPOS.Data.Security;

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

    // ---- Padrón (FR-ADM-2): un alumno inscrito en el POS debe llegar a la nube ----

    /// <summary>
    /// Sin esto, un alumno dado de alta en el POS no existía en la nube: su tutor no podía
    /// vincularlo por matrícula y, aunque pudiera, GuardianService.LinkStudentByEnrollmentAsync
    /// consulta la tabla Students de la nube directamente — sin fila, sin vínculo posible.
    /// </summary>
    [Fact]
    public async Task Locally_enrolled_student_becomes_linkable_by_the_parent_after_sync()
    {
        using var cloud = new TestDatabase();
        using var local = new TestDatabase();
        var school = local.SeedSchool();
        cloud.Context.Schools.Add(new SchoolPOS.Domain.Entities.School
        {
            Id = school.Id, Name = school.Name, Currency = "MXN", CommissionRate = 0.05m,
        });
        await cloud.Context.SaveChangesAsync();

        var registry = new StudentRegistry(local.Context, new TestClock());
        var student = await registry.CreateAsync(school.Id, "A-900", "Nuevo Alumno", cardCode: "CARD-900");

        var pushed = await NewAgent(cloud, local).PushRosterAsync();

        pushed.Should().Be(1);
        var cloudCtx = cloud.NewContext();
        var cloudStudent = await cloudCtx.Students.SingleAsync(s => s.Id == student.Id);
        cloudStudent.EnrollmentNo.Should().Be("A-900");
        cloudStudent.CardCode.Should().Be("CARD-900");
        (await cloudCtx.Accounts.Where(a => a.StudentId == student.Id).Select(a => a.Balance).SingleAsync())
            .Should().Be(0m, "un alumno nuevo llega a la nube en $0.00, igual que en la escuela");

        // Y ahora sí se puede vincular por matrícula, como hace el portal.
        var guardians = new GuardianService(cloudCtx, new Pbkdf2PasswordHasher(), new TestClock());
        var guardian = await guardians.RegisterAsync(school.Id, "padre@correo.mx", "clave123", "Padre");
        await guardians.LinkStudentByEnrollmentAsync(guardian.Id, school.Id, "A-900");
        (await guardians.GetLinkedStudentsAsync(guardian.Id)).Should().ContainSingle();
    }

    /// <summary>
    /// El padrón se reconcilia completo cada corrida (no hay marca de "ya subido"), así que un
    /// cambio hecho en el POS después del primer alta — renombrar, asignar credencial, dar de
    /// baja — también debe llegar.
    /// </summary>
    [Fact]
    public async Task Roster_edits_made_after_the_first_sync_are_also_pushed()
    {
        using var cloud = new TestDatabase();
        using var local = new TestDatabase();
        var school = local.SeedSchool();
        cloud.Context.Schools.Add(new SchoolPOS.Domain.Entities.School
        {
            Id = school.Id, Name = school.Name, Currency = "MXN", CommissionRate = 0.05m,
        });
        await cloud.Context.SaveChangesAsync();

        var registry = new StudentRegistry(local.Context, new TestClock());
        var student = await registry.CreateAsync(school.Id, "A-901", "Nombre Viejo", cardCode: null);
        var agent = NewAgent(cloud, local);
        await agent.PushRosterAsync();

        await registry.UpdateAsync(student.Id, "A-901", "Nombre Corregido", cardCode: "CARD-901");
        await registry.SetActiveAsync(student.Id, false);
        var pushed = await agent.PushRosterAsync();

        pushed.Should().Be(1);
        var cloudStudent = await cloud.NewContext().Students.SingleAsync(s => s.Id == student.Id);
        cloudStudent.FullName.Should().Be("Nombre Corregido");
        cloudStudent.CardCode.Should().Be("CARD-901");
        cloudStudent.IsActive.Should().BeFalse();
    }

    /// <summary>Sin cambios en el padrón, la corrida no debe volver a escribir nada.</summary>
    [Fact]
    public async Task Roster_push_is_a_no_op_once_the_cloud_matches()
    {
        using var cloud = new TestDatabase();
        using var local = new TestDatabase();
        var school = local.SeedSchool();
        cloud.Context.Schools.Add(new SchoolPOS.Domain.Entities.School
        {
            Id = school.Id, Name = school.Name, Currency = "MXN", CommissionRate = 0.05m,
        });
        await cloud.Context.SaveChangesAsync();

        var registry = new StudentRegistry(local.Context, new TestClock());
        await registry.CreateAsync(school.Id, "A-902", "Alumno Estable", cardCode: "CARD-902");
        var agent = NewAgent(cloud, local);
        await agent.PushRosterAsync();

        (await agent.PushRosterAsync()).Should().Be(0);
    }

    /// <summary>
    /// Antes de esta corrección, una venta al alumno recién inscrito quedaba descartada por
    /// PushConsumptionAsync (la cuenta no existía en la nube) y el tutor nunca la veía. Con el
    /// padrón subiendo primero, en la MISMA corrida, la venta ya no debería quedar pendiente.
    /// </summary>
    [Fact]
    public async Task A_sale_for_a_brand_new_local_student_reaches_the_cloud_in_the_same_run()
    {
        using var cloud = new TestDatabase();
        using var local = new TestDatabase();
        var school = local.SeedSchool();
        cloud.Context.Schools.Add(new SchoolPOS.Domain.Entities.School
        {
            Id = school.Id, Name = school.Name, Currency = "MXN", CommissionRate = 0.05m,
        });
        await cloud.Context.SaveChangesAsync();

        var registry = new StudentRegistry(local.Context, new TestClock());
        var student = await registry.CreateAsync(school.Id, "A-903", "Alumno Sale Mismo Ciclo", null);
        var account = (await registry.ListAsync(school.Id)).Single().AccountId;

        var clock = new TestClock();
        var localBalance = new BalanceService(local.Context, clock);
        await localBalance.AdjustAsync(account, 100m, "Fondeo inicial de prueba", Guid.NewGuid());
        await localBalance.ChargeSaleAsync(account, 40m, "VENTA-903", Guid.NewGuid());

        var agent = new SyncAgent(cloud.Context, local.Context, localBalance, clock);
        var report = await agent.RunOnceAsync();

        report.RosterPushed.Should().Be(1);
        report.MovementsSkipped.Should().Be(0, "el padrón ya subió antes de intentar el consumo");
        report.MovementsPushed.Should().Be(2, "el ajuste y la venta");
        (await cloud.NewContext().Accounts.Where(a => a.Id == account).Select(a => a.Balance).SingleAsync())
            .Should().Be(60m);
    }
}
