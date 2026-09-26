using Microsoft.EntityFrameworkCore;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Entities;
using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Data.Sync;

/// <summary>
/// Agente de sincronización nube ↔ DB local de una escuela (Fase 3.C). La DB local es la
/// <b>fuente única de verdad</b> del saldo; la nube (portal) origina las recargas. El agente:
/// <list type="number">
///   <item>baja las recargas <b>confirmadas</b> y las aplica al libro mayor local de forma
///   idempotente (3.16), acusando recibo en la nube;</item>
///   <item>sube el consumo local (ventas/devoluciones) a la nube para que el padre lo vea (3.17).</item>
/// </list>
/// Cada recarga se procesa de forma aislada: si la DB local no está disponible (escuela offline),
/// la recarga queda pendiente en la nube y se aplica al reconectar (3.18); nunca se pierde ni se
/// duplica (dedupe por <c>gateway_ref</c> + bandera <c>AppliedLocally</c> + idempotencia del ledger).
/// <para>
/// La nube se ve a través de <see cref="ISyncApiClient"/>, no de una segunda conexión a base de
/// datos: el agente corre en la escuela y solo tiene la llave de <c>/api/sync/*</c>, nunca
/// credenciales de base de datos (FR-SYNC-API). Toda la lógica de esta clase — orden de
/// operaciones, deduplicación, reintentos — es la misma sin importar si esa interfaz habla HTTP
/// (producción) o envuelve el servicio del portal directamente (pruebas).
/// </para>
/// </summary>
public sealed class SyncAgent
{
    private readonly ISyncApiClient _cloud;
    private readonly SchoolDbContext _local;
    private readonly IBalanceService _localBalance;
    private readonly IClock _clock;

    // Consumo: nace siempre en la escuela y viaja hacia la nube.
    private static readonly MovementType[] Consumption =
        { MovementType.Sale, MovementType.Refund, MovementType.Adjustment };

    public SyncAgent(ISyncApiClient cloud, SchoolDbContext local, IBalanceService localBalance, IClock clock)
    {
        _cloud = cloud;
        _local = local;
        _localBalance = localBalance;
        _clock = clock;
    }

    /// <summary>
    /// Tamaño máximo de asientos a subir por corrida. Acota el trabajo de cada ciclo (y la primera
    /// corrida tras actualizar, cuando todo el historial está aún sin marcar); lo que no entra se
    /// sube en la siguiente.
    /// </summary>
    private const int PushBatchSize = 500;

    /// <summary>Ejecuta una corrida completa (bajar recargas + subir consumo) y devuelve el estado.</summary>
    public async Task<SyncReport> RunOnceAsync(CancellationToken ct = default)
    {
        var (pulled, applied, failed) = await PullTopUpsAsync(ct);
        var rosterPushed = await PushRosterAsync(ct);
        var (pushed, skipped) = await PushConsumptionAsync(ct);
        var salesPushed = await PushSalesAsync(ct);
        var limitsUpdated = await PullAccountLimitsAsync(ct);
        var ordersApplied = await PullOrdersAsync(ct);
        return new SyncReport(
            pulled, applied, failed, pushed, skipped, rosterPushed, _clock.UtcNow, limitsUpdated, salesPushed,
            ordersApplied);
    }

    /// <summary>Baja recargas confirmadas y las aplica al libro mayor local (idempotente).</summary>
    public async Task<(int Pulled, int Applied, int Failed)> PullTopUpsAsync(CancellationToken ct = default)
    {
        var confirmed = await _cloud.GetPendingTopUpsAsync(ct);

        int applied = 0, failed = 0;
        var acked = new List<Guid>();
        foreach (var cloudTopUp in confirmed)
        {
            try
            {
                var localId = await EnsureLocalTopUpAsync(cloudTopUp, ct);
                await _localBalance.ApplyTopUpAsync(localId, ct); // acredita 100%, idempotente
                acked.Add(cloudTopUp.Id);
                applied++;
            }
            catch (Exception)
            {
                // Escuela offline o roster local incompleto: se reintenta en la próxima corrida.
                failed++;
            }
        }

        // Acuse en lote: si falla, la próxima corrida vuelve a ver estas recargas como pendientes
        // y las reintenta — seguro, porque tanto EnsureLocalTopUpAsync (dedupe por gateway_ref)
        // como ApplyTopUpAsync (idempotente) toleran reprocesar una ya aplicada.
        if (acked.Count > 0)
            await _cloud.AckTopUpsAsync(acked, ct);

        return (confirmed.Count, applied, failed);
    }

    /// <summary>
    /// Sube a la nube lo que nació en la escuela: ventas, devoluciones, ajustes y las recargas
    /// <b>en efectivo</b> capturadas en el mostrador. Solo lee lo <b>pendiente</b>
    /// (<c>SyncedToCloudAtUtc == null</c>) y en lotes acotados: releer todo el historial en cada
    /// ciclo hacía que el costo creciera sin límite con la antigüedad de la escuela.
    /// <para>
    /// Las recargas <b>por pasarela</b> se excluyen a propósito: esas nacen en el portal, que ya
    /// asentó allá su propio movimiento al confirmarlas. Subir el asiento local las duplicaría en
    /// el saldo que ve el tutor. Aun así se marcan como sincronizadas para que no se queden en la
    /// cola reexaminándose en cada corrida.
    /// </para>
    /// </summary>
    /// <returns>
    /// Cuántos asientos se subieron y cuántos se omitieron porque el roster de la nube todavía no
    /// tiene su cuenta (quedan pendientes y se reintentan en la próxima corrida).
    /// </returns>
    public async Task<(int Pushed, int Skipped)> PushConsumptionAsync(CancellationToken ct = default)
    {
        var pending = await _local.BalanceMovements
            .Where(m => m.SyncedToCloudAtUtc == null &&
                        (Consumption.Contains(m.Type) || m.Type == MovementType.TopUp))
            .OrderBy(m => m.CreatedAtUtc)
            .Take(PushBatchSize)
            .ToListAsync(ct);
        if (pending.Count == 0)
            return (0, 0);

        // De las recargas del lote, cuáles son de efectivo (las únicas que sí deben subir).
        var topUpRefs = pending
            .Where(m => m.Type == MovementType.TopUp && m.Reference != null)
            .Select(m => m.Reference!)
            .Distinct()
            .ToList();
        var cashRefs = topUpRefs.Count == 0
            ? new HashSet<string>()
            : (await _local.TopUps.AsNoTracking()
                .Where(t => t.Origin == TopUpOrigin.Cash && topUpRefs.Contains(t.GatewayRef))
                .Select(t => t.GatewayRef)
                .ToListAsync(ct)).ToHashSet();

        var now = _clock.UtcNow;
        var toSend = new List<ConsumptionEntryDto>();

        foreach (var m in pending)
        {
            // Recarga por pasarela: ya está en la nube por su propio camino. Se marca y no sube.
            if (m.Type == MovementType.TopUp && (m.Reference is null || !cashRefs.Contains(m.Reference)))
            {
                m.SyncedToCloudAtUtc = now;
                continue;
            }

            toSend.Add(new ConsumptionEntryDto(
                m.Id, m.AccountId, m.Type, m.Amount, m.BalanceAfter, m.Reference, m.OperatorId, m.CreatedAtUtc));
        }

        int pushed = 0, skipped = 0;
        if (toSend.Count > 0)
        {
            var result = await _cloud.PushConsumptionAsync(toSend, ct);
            var applied = result.Applied.ToHashSet();
            pushed = result.Applied.Count;
            skipped = result.Skipped.Count;

            foreach (var m in pending)
            {
                if (applied.Contains(m.Id))
                    m.SyncedToCloudAtUtc = now;
                // Lo "skipped" queda sin marcar a propósito: el roster de la nube aún no tiene la
                // cuenta, y sin marcar se reintenta tal cual en la próxima corrida.
            }
        }

        await _local.SaveChangesAsync(ct);
        return (pushed, skipped);
    }

    /// <summary>Inserta la recarga en la DB local si no existe (dedupe por gateway_ref) y devuelve su Id.</summary>
    private async Task<Guid> EnsureLocalTopUpAsync(PendingTopUpDto cloudTopUp, CancellationToken ct)
    {
        var existingId = await _local.TopUps
            .Where(t => t.GatewayRef == cloudTopUp.GatewayRef)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(ct);
        if (existingId is { } id)
            return id;

        _local.TopUps.Add(new TopUp
        {
            Id = cloudTopUp.Id,
            SchoolId = cloudTopUp.SchoolId,
            AccountId = cloudTopUp.AccountId,
            Amount = cloudTopUp.Amount,
            CommissionRate = cloudTopUp.CommissionRate,
            CommissionAmount = cloudTopUp.CommissionAmount,
            GatewayRef = cloudTopUp.GatewayRef,
            Status = TopUpStatus.Confirmed,
            AppliedLocally = false,
            CreatedAtUtc = cloudTopUp.CreatedAtUtc,
        });
        await _local.SaveChangesAsync(ct);
        return cloudTopUp.Id;
    }

    /// <summary>
    /// Sube el padrón (alumnos + su cuenta) que nació en la escuela: FR-ADM-2 lo da de alta en el
    /// POS, no en el portal, así que sin esto un alumno inscrito en la caja no existía para su
    /// tutor — ni podía vincularlo por matrícula ni sus movimientos podían subir (los quedaba
    /// descartando <see cref="PushConsumptionAsync"/> por no encontrar la cuenta en la nube).
    /// <para>
    /// A diferencia del consumo, aquí no hace falta una marca de "ya sincronizado": el padrón de
    /// una escuela es acotado (cientos o miles de alumnos, no crece sin límite como el historial de
    /// ventas), así que reconciliar el padrón completo en cada corrida es barato y además
    /// autocorrectivo — recoge también renombres, cambios de credencial y bajas/altas hechos en el
    /// POS después de la primera sincronización, que una marca de una sola vez se perdería.
    /// </para>
    /// <para>
    /// El saldo de la cuenta nunca se toca aquí. Un alumno siempre nace con saldo $0 (el alta lo
    /// garantiza) y cualquier movimiento posterior sube por <see cref="PushConsumptionAsync"/>;
    /// mezclar los dos caminos podría pisar un saldo que el portal ya adelantó.
    /// </para>
    /// </summary>
    /// <returns>Cuántos alumnos se crearon o actualizaron en la nube.</returns>
    public async Task<int> PushRosterAsync(CancellationToken ct = default)
    {
        var locals = await (
            from s in _local.Students.AsNoTracking()
            join a in _local.Accounts.AsNoTracking() on s.Id equals a.StudentId
            select new RosterEntryDto(
                s.Id, s.EnrollmentNo, s.CardCode, s.FullName, s.IsActive, s.CreatedAtUtc, a.Id, s.Grade))
            .ToListAsync(ct);
        if (locals.Count == 0)
            return 0;

        var result = await _cloud.PushRosterAsync(locals, ct);
        return result.Pushed;
    }

    /// <summary>
    /// Baja el presupuesto diario que cada tutor fijó desde el portal — a diferencia del padrón y
    /// el consumo, este dato nace en la nube, no en la escuela, así que el sentido es el opuesto:
    /// nube→escuela. Reconciliación completa cada corrida, igual que <see cref="PushRosterAsync"/>
    /// y por el mismo motivo (número de cuentas acotado): también recoge que un tutor quite el
    /// límite (vuelva a <c>null</c>), no solo que lo ponga o lo cambie.
    /// </summary>
    /// <returns>Cuántas cuentas locales cambiaron de valor.</returns>
    public async Task<int> PullAccountLimitsAsync(CancellationToken ct = default)
    {
        var limits = await _cloud.GetAccountLimitsAsync(ct);
        if (limits.Count == 0)
            return 0;

        var updated = 0;
        foreach (var l in limits)
        {
            var affected = await _local.Accounts
                .Where(a => a.Id == l.AccountId && a.DailySpendLimit != l.DailySpendLimit)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.DailySpendLimit, l.DailySpendLimit), ct);
            updated += affected;
        }
        return updated;
    }

    /// <summary>
    /// Sube las ventas (con renglones) que nacieron en esta escuela: para que el tutor vea qué
    /// compró el alumno, no solo el importe del BalanceMovement, y para el reporte de ventas de la
    /// escuela en el portal. Igual patrón que <see cref="PushConsumptionAsync"/> (marca de
    /// pendiente + lote acotado), porque el historial de ventas también crece sin límite.
    /// </summary>
    public async Task<int> PushSalesAsync(CancellationToken ct = default)
    {
        var pending = await _local.Sales
            .Include(s => s.Lines)
            .Where(s => s.SyncedToCloudAtUtc == null)
            .OrderBy(s => s.CreatedAtUtc)
            .Take(PushBatchSize)
            .ToListAsync(ct);
        if (pending.Count == 0)
            return 0;

        var toSend = pending.Select(s => new SaleEntryDto(
            s.Id, s.StudentId, s.AccountId, s.Tender, s.Status,
            s.Subtotal, s.DiscountTotal, s.TaxTotal, s.Total, s.AmountTendered, s.CreatedAtUtc,
            s.Lines.Select(l => new SaleLineEntryDto(
                l.Id, l.ProductId, l.Description, l.Quantity, l.UnitPrice, l.Discount, l.LineTotal)).ToList()))
            .ToList();

        var result = await _cloud.PushSalesAsync(toSend, ct);
        var applied = result.Applied.ToHashSet();
        var now = _clock.UtcNow;

        foreach (var s in pending)
        {
            if (applied.Contains(s.Id))
                s.SyncedToCloudAtUtc = now;
            // Lo "skipped" queda sin marcar a propósito, igual que en PushConsumptionAsync: el
            // padrón de la nube aún no tiene la cuenta, se reintenta tal cual en la próxima corrida.
        }

        await _local.SaveChangesAsync(ct);
        return applied.Count;
    }

    // Sin operador físico: el pedido nace en el portal, no en la caja — mismo centinela que usa
    // PortalOrderService del lado de la nube.
    private static readonly Guid PortalOperator = Guid.Empty;

    /// <summary>
    /// Baja los pedidos anticipados ya cobrados en la nube y los aplica al libro mayor local —
    /// mismo camino que una recarga confirmada (<see cref="PullTopUpsAsync"/>), solo que resta en
    /// vez de sumar. A diferencia de una recarga, no hay una entidad local que dar de alta primero
    /// (el pedido no crea una Sale local en esta versión — ver nota de alcance); la idempotencia se
    /// verifica buscando si ya existe un asiento local con esta referencia, porque
    /// <see cref="IBalanceService.ChargeSaleAsync"/>, a diferencia de ApplyTopUpAsync, no es
    /// idempotente por sí solo.
    /// </summary>
    public async Task<int> PullOrdersAsync(CancellationToken ct = default)
    {
        var pending = await _cloud.GetPendingOrdersAsync(ct);

        var acked = new List<Guid>();
        foreach (var order in pending)
        {
            try
            {
                var reference = order.Id.ToString();
                var already = await _local.BalanceMovements.AsNoTracking()
                    .AnyAsync(m => m.Type == MovementType.Sale && m.Reference == reference, ct);
                if (!already)
                    await _localBalance.ChargeSaleAsync(order.AccountId, order.Total, reference, PortalOperator, ct);

                acked.Add(order.Id);
            }
            catch (Exception)
            {
                // Cuenta aún no sincronizada, saldo insuficiente, etc.: se reintenta en la próxima
                // corrida (no se acusa, así que sigue apareciendo como pendiente).
            }
        }

        if (acked.Count > 0)
            await _cloud.AckOrdersAsync(acked, ct);

        return acked.Count;
    }
}
