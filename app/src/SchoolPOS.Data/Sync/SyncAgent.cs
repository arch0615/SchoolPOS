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
/// </summary>
public sealed class SyncAgent
{
    private readonly SchoolDbContext _cloud;
    private readonly SchoolDbContext _local;
    private readonly IBalanceService _localBalance;
    private readonly IClock _clock;

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
        var (pushed, skipped) = await PushConsumptionAsync(ct);
        return new SyncReport(pulled, applied, failed, pushed, skipped, _clock.UtcNow);
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
    /// Sube el consumo local (ventas/devoluciones/ajustes) a la nube para el portal del padre.
    /// Solo lee lo <b>pendiente</b> (<c>SyncedToCloudAtUtc == null</c>) y en lotes acotados: releer
    /// todo el historial en cada ciclo hacía que el costo de la sincronización creciera sin límite
    /// con la antigüedad de la escuela.
    /// </summary>
    /// <returns>
    /// Cuántos asientos se subieron y cuántos se omitieron porque el roster de la nube todavía no
    /// tiene su cuenta (quedan pendientes y se reintentan en la próxima corrida).
    /// </returns>
    public async Task<(int Pushed, int Skipped)> PushConsumptionAsync(CancellationToken ct = default)
    {
        var pending = await _local.BalanceMovements
            .Where(m => m.SyncedToCloudAtUtc == null && Consumption.Contains(m.Type))
            .OrderBy(m => m.CreatedAtUtc)
            .Take(PushBatchSize)
            .ToListAsync(ct);
        if (pending.Count == 0)
            return (0, 0);

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
        foreach (var m in pending)
        {
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
                pushed++;
            }

            m.SyncedToCloudAtUtc = now;
        }

        // Orden deliberado: primero la nube, después la marca local. Si el guardado en la nube
        // falla, no queda nada marcado y el lote entero se reintenta; el peor caso es reintentar
        // un asiento ya subido, que el dedupe por Id de arriba vuelve a filtrar.
        if (pushed > 0)
            await _cloud.SaveChangesAsync(ct);
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
}
