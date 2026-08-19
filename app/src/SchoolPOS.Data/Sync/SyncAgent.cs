using Microsoft.EntityFrameworkCore;
using SchoolPOS.Data.Services;
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
/// </summary>
public sealed class SyncAgent
{
    private readonly SchoolDbContext _cloud;
    private readonly SchoolDbContext _local;
    private readonly IBalanceService _localBalance;
    private readonly IClock _clock;

    // Consumo: nace siempre en la escuela y viaja hacia la nube.
    private static readonly MovementType[] Consumption =
        { MovementType.Sale, MovementType.Refund, MovementType.Adjustment };

    public SyncAgent(SchoolDbContext cloud, SchoolDbContext local, IBalanceService localBalance, IClock clock)
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
        return new SyncReport(pulled, applied, failed, pushed, skipped, rosterPushed, _clock.UtcNow);
    }

    /// <summary>Baja recargas confirmadas y las aplica al libro mayor local (idempotente).</summary>
    public async Task<(int Pulled, int Applied, int Failed)> PullTopUpsAsync(CancellationToken ct = default)
    {
        var confirmed = await _cloud.TopUps
            .Where(t => t.Status == TopUpStatus.Confirmed && !t.AppliedLocally)
            .ToListAsync(ct);

        int applied = 0, failed = 0;
        foreach (var cloudTopUp in confirmed)
        {
            try
            {
                var localId = await EnsureLocalTopUpAsync(cloudTopUp, ct);
                await _localBalance.ApplyTopUpAsync(localId, ct); // acredita 100%, idempotente

                // Acuse en la nube: evita reprocesar.
                cloudTopUp.Status = TopUpStatus.Applied;
                cloudTopUp.AppliedLocally = true;
                cloudTopUp.AppliedAtUtc = _clock.UtcNow;
                await _cloud.SaveChangesAsync(ct);
                applied++;
            }
            catch (Exception)
            {
                // Escuela offline o roster local incompleto: se reintenta en la próxima corrida.
                failed++;
            }
        }

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

        // Ya presentes en la nube: asientos subidos antes de que existiera la marca (o por una
        // corrida que murió entre el guardado de la nube y el de la marca local).
        var ids = pending.Select(m => m.Id).ToList();
        var alreadyInCloud = (await _cloud.BalanceMovements.AsNoTracking()
            .Where(m => ids.Contains(m.Id)).Select(m => m.Id).ToListAsync(ct)).ToHashSet();

        var accountIds = pending.Select(m => m.AccountId).Distinct().ToList();
        var cloudAccounts = (await _cloud.Accounts.AsNoTracking()
            .Where(a => accountIds.Contains(a.Id)).Select(a => a.Id).ToListAsync(ct)).ToHashSet();

        var now = _clock.UtcNow;
        int pushed = 0, skipped = 0;

        // Variación de saldo por cuenta, para aplicarla a la nube junto con los asientos. Se
        // acumula solo de lo que realmente se inserta: reaplicarla a un asiento que ya estaba
        // arriba lo contaría dos veces.
        var deltas = new Dictionary<Guid, decimal>();

        foreach (var m in pending)
        {
            // Recarga por pasarela: ya está en la nube por su propio camino. Se marca y no sube.
            if (m.Type == MovementType.TopUp &&
                (m.Reference is null || !cashRefs.Contains(m.Reference)))
            {
                m.SyncedToCloudAtUtc = now;
                continue;
            }

            if (!cloudAccounts.Contains(m.AccountId))
            {
                skipped++; // el roster de la nube aún no tiene la cuenta: sin marcar, se reintenta.
                continue;
            }

            if (!alreadyInCloud.Contains(m.Id))
            {
                _cloud.BalanceMovements.Add(new BalanceMovement
                {
                    Id = m.Id,
                    AccountId = m.AccountId,
                    Type = m.Type,
                    Amount = m.Amount,
                    BalanceAfter = m.BalanceAfter,
                    Reference = m.Reference,
                    OperatorId = m.OperatorId,
                    CreatedAtUtc = m.CreatedAtUtc,
                });
                deltas[m.AccountId] = deltas.GetValueOrDefault(m.AccountId) + m.Amount;
                pushed++;
            }

            m.SyncedToCloudAtUtc = now;
        }

        // Orden deliberado: primero la nube, después la marca local. Si el guardado en la nube
        // falla, no queda nada marcado y el lote entero se reintenta; el peor caso es reintentar
        // un asiento ya subido, que el dedupe por Id de arriba vuelve a filtrar.
        if (pushed > 0)
            await _cloud.ExecuteAtomicAsync(async () =>
            {
                await _cloud.SaveChangesAsync(ct);

                // El saldo de la cuenta en la nube es lo que el portal le muestra al tutor. Sin
                // esto solo subían los asientos: la lista de movimientos mostraba la compra pero
                // el saldo seguía igual y de más, y las dos cifras de la misma pantalla se
                // contradecían de forma permanente.
                //
                // Se aplica la VARIACIÓN, no el BalanceAfter local: si la nube ya confirmó una
                // recarga que la escuela todavía no baja, su saldo va por delante a propósito y
                // copiar el local borraría dinero que el tutor ya pagó. El UPDATE condicional
                // (Balance + delta) además no pierde una recarga que el portal aplique a la vez.
                foreach (var (accountId, delta) in deltas)
                {
                    await _cloud.Accounts
                        .Where(a => a.Id == accountId)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(a => a.Balance, a => a.Balance + delta)
                            .SetProperty(a => a.UpdatedAtUtc, now), ct);
                }

                return 0;
            }, ct);
        await _local.SaveChangesAsync(ct);

        return (pushed, skipped);
    }

    /// <summary>Inserta la recarga en la DB local si no existe (dedupe por gateway_ref) y devuelve su Id.</summary>
    private async Task<Guid> EnsureLocalTopUpAsync(TopUp cloudTopUp, CancellationToken ct)
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
            select new
            {
                s.Id, s.SchoolId, s.EnrollmentNo, s.CardCode, s.FullName, s.IsActive, s.CreatedAtUtc,
                AccountId = a.Id,
            })
            .ToListAsync(ct);
        if (locals.Count == 0)
            return 0;

        var ids = locals.Select(l => l.Id).ToList();
        var cloudStudents = await _cloud.Students
            .Where(s => ids.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, ct);

        var pushed = 0;
        foreach (var l in locals)
        {
            try
            {
                if (cloudStudents.TryGetValue(l.Id, out var existing))
                {
                    // Solo lo que el POS puede editar después del alta. Balance/OverdraftLimit de
                    // la cuenta quedan fuera a propósito: eso lo gobierna el consumo, no el padrón.
                    if (existing.FullName == l.FullName && existing.EnrollmentNo == l.EnrollmentNo &&
                        existing.CardCode == l.CardCode && existing.IsActive == l.IsActive)
                        continue;

                    existing.FullName = l.FullName;
                    existing.EnrollmentNo = l.EnrollmentNo;
                    existing.CardCode = l.CardCode;
                    existing.IsActive = l.IsActive;
                }
                else
                {
                    _cloud.Students.Add(new Student
                    {
                        Id = l.Id,
                        SchoolId = l.SchoolId,
                        EnrollmentNo = l.EnrollmentNo,
                        CardCode = l.CardCode,
                        FullName = l.FullName,
                        IsActive = l.IsActive,
                        CreatedAtUtc = l.CreatedAtUtc,
                    });
                    _cloud.Accounts.Add(new Account
                    {
                        Id = l.AccountId,
                        StudentId = l.Id,
                        Balance = 0m,
                        OverdraftLimit = 0m,
                        UpdatedAtUtc = _clock.UtcNow,
                    });
                }

                // Cada alumno en su propio SaveChanges: una matrícula o credencial que choque con
                // otra escuela (o un valor manipulado a mano en la nube) no debe tumbar el resto
                // del padrón, igual que cada recarga se procesa aislada en PullTopUpsAsync.
                await _cloud.SaveChangesAsync(ct);
                pushed++;
            }
            catch (Exception)
            {
                // Fila en conflicto (índice único, etc.): se reintenta en la próxima corrida tal
                // cual está en la escuela; no hay nada que descartar ni marcar.
            }
        }

        return pushed;
    }
}
