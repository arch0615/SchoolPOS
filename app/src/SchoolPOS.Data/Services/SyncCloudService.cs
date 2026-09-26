using Microsoft.EntityFrameworkCore;
using SchoolPOS.Data.Sync;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Entities;
using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Data.Services;

/// <inheritdoc cref="ISyncCloudService"/>
public sealed class SyncCloudService : ISyncCloudService
{
    private readonly SchoolDbContext _db;
    private readonly IClock _clock;

    public SyncCloudService(SchoolDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<IReadOnlyList<PendingTopUpDto>> GetPendingTopUpsAsync(Guid schoolId, CancellationToken ct = default) =>
        await _db.TopUps.AsNoTracking()
            .Where(t => t.SchoolId == schoolId && t.Status == TopUpStatus.Confirmed && !t.AppliedLocally)
            .Select(t => new PendingTopUpDto(
                t.Id, t.SchoolId, t.AccountId, t.Amount, t.CommissionRate, t.CommissionAmount, t.GatewayRef, t.CreatedAtUtc))
            .ToListAsync(ct);

    public async Task AckTopUpsAsync(Guid schoolId, IReadOnlyList<Guid> topUpIds, CancellationToken ct = default)
    {
        if (topUpIds.Count == 0)
            return;

        var now = _clock.UtcNow;
        // Filtrado por schoolId: un id ajeno (manipulado o de otra corrida) no toca nada aquí.
        var rows = await _db.TopUps
            .Where(t => t.SchoolId == schoolId && topUpIds.Contains(t.Id))
            .ToListAsync(ct);
        foreach (var t in rows)
        {
            t.Status = TopUpStatus.Applied;
            t.AppliedLocally = true;
            t.AppliedAtUtc = now;
        }
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Misma lógica que el lado nube de <c>SyncAgent.PushRosterAsync</c> (reconciliación completa, sin marca de "ya sincronizado"), corriendo ahora del lado del portal.</summary>
    public async Task<RosterPushResult> PushRosterAsync(
        Guid schoolId, IReadOnlyList<RosterEntryDto> entries, CancellationToken ct = default)
    {
        if (entries.Count == 0)
            return new RosterPushResult(0);

        var ids = entries.Select(e => e.Id).ToList();
        var existing = await _db.Students
            .Where(s => s.SchoolId == schoolId && ids.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, ct);

        var pushed = 0;
        foreach (var e in entries)
        {
            try
            {
                if (existing.TryGetValue(e.Id, out var student))
                {
                    if (student.FullName == e.FullName && student.EnrollmentNo == e.EnrollmentNo &&
                        student.CardCode == e.CardCode && student.IsActive == e.IsActive &&
                        student.Grade == e.Grade)
                        continue;

                    student.FullName = e.FullName;
                    student.EnrollmentNo = e.EnrollmentNo;
                    student.CardCode = e.CardCode;
                    student.IsActive = e.IsActive;
                    student.Grade = e.Grade;
                }
                else
                {
                    _db.Students.Add(new Student
                    {
                        Id = e.Id,
                        SchoolId = schoolId,
                        EnrollmentNo = e.EnrollmentNo,
                        CardCode = e.CardCode,
                        FullName = e.FullName,
                        IsActive = e.IsActive,
                        Grade = e.Grade,
                        CreatedAtUtc = e.CreatedAtUtc,
                    });
                    _db.Accounts.Add(new Account
                    {
                        Id = e.AccountId,
                        StudentId = e.Id,
                        Balance = 0m,
                        OverdraftLimit = 0m,
                        UpdatedAtUtc = _clock.UtcNow,
                    });
                }

                // Cada alumno en su propio SaveChanges: una fila en conflicto no debe tumbar el
                // resto del lote — se reintenta tal cual en la próxima corrida.
                await _db.SaveChangesAsync(ct);
                pushed++;
            }
            catch (DbUpdateException)
            {
            }
        }

        return new RosterPushResult(pushed);
    }

    /// <summary>Misma lógica que el lado nube de <c>SyncAgent.PushConsumptionAsync</c>, corriendo ahora del lado del portal.</summary>
    public async Task<ConsumptionPushResult> PushConsumptionAsync(
        Guid schoolId, IReadOnlyList<ConsumptionEntryDto> entries, CancellationToken ct = default)
    {
        if (entries.Count == 0)
            return new ConsumptionPushResult(new List<Guid>(), new List<Guid>());

        var ids = entries.Select(e => e.Id).ToList();
        var alreadyInCloud = (await _db.BalanceMovements.AsNoTracking()
            .Where(m => ids.Contains(m.Id)).Select(m => m.Id).ToListAsync(ct)).ToHashSet();

        var accountIds = entries.Select(e => e.AccountId).Distinct().ToList();
        // Solo cuentas de ESTA escuela: aunque el cliente mande un AccountId ajeno (por error o a
        // propósito), nunca se toca el saldo de otra escuela.
        var validAccounts = (await _db.Accounts.AsNoTracking()
            .Where(a => accountIds.Contains(a.Id) && a.Student.SchoolId == schoolId)
            .Select(a => a.Id).ToListAsync(ct)).ToHashSet();

        var applied = new List<Guid>();
        var skipped = new List<Guid>();
        var deltas = new Dictionary<Guid, decimal>();
        var now = _clock.UtcNow;

        foreach (var e in entries)
        {
            if (!validAccounts.Contains(e.AccountId))
            {
                skipped.Add(e.Id); // padrón aún desfasado: el cliente lo reintenta en la próxima corrida.
                continue;
            }

            if (!alreadyInCloud.Contains(e.Id))
            {
                _db.BalanceMovements.Add(new BalanceMovement
                {
                    Id = e.Id,
                    AccountId = e.AccountId,
                    Type = e.Type,
                    Amount = e.Amount,
                    BalanceAfter = e.BalanceAfter,
                    Reference = e.Reference,
                    OperatorId = e.OperatorId,
                    CreatedAtUtc = e.CreatedAtUtc,
                });
                deltas[e.AccountId] = deltas.GetValueOrDefault(e.AccountId) + e.Amount;
            }
            applied.Add(e.Id);
        }

        if (applied.Count > 0)
            await _db.ExecuteAtomicAsync(async () =>
            {
                await _db.SaveChangesAsync(ct);

                // Variación de saldo, no el valor absoluto local: si el portal ya adelantó una
                // recarga que la escuela aún no bajó, copiar el saldo local borraría dinero que el
                // tutor ya pagó (mismo razonamiento que en SyncAgent.PushConsumptionAsync).
                foreach (var (accountId, delta) in deltas)
                {
                    await _db.Accounts
                        .Where(a => a.Id == accountId)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(a => a.Balance, a => a.Balance + delta)
                            .SetProperty(a => a.UpdatedAtUtc, now), ct);
                }

                return 0;
            }, ct);

        return new ConsumptionPushResult(applied, skipped);
    }

    public async Task<IReadOnlyList<AccountLimitDto>> GetAccountLimitsAsync(Guid schoolId, CancellationToken ct = default) =>
        await (
            from a in _db.Accounts.AsNoTracking()
            join s in _db.Students.AsNoTracking() on a.StudentId equals s.Id
            where s.SchoolId == schoolId
            select new AccountLimitDto(a.Id, a.DailySpendLimit))
            .ToListAsync(ct);

    public async Task<SalesPushResult> PushSalesAsync(
        Guid schoolId, IReadOnlyList<SaleEntryDto> entries, CancellationToken ct = default)
    {
        if (entries.Count == 0)
            return new SalesPushResult(new List<Guid>(), new List<Guid>());

        var ids = entries.Select(e => e.Id).ToList();
        var alreadyInCloud = (await _db.Sales.AsNoTracking()
            .Where(s => ids.Contains(s.Id)).Select(s => s.Id).ToListAsync(ct)).ToHashSet();

        // Solo cuentas de ESTA escuela: aunque el cliente mande un AccountId ajeno, nunca se le
        // atribuye una venta a otra escuela. Nula (venta de mostrador en efectivo) siempre vale.
        var accountIds = entries.Where(e => e.AccountId is not null).Select(e => e.AccountId!.Value).Distinct().ToList();
        var validAccounts = accountIds.Count == 0
            ? new HashSet<Guid>()
            : (await _db.Accounts.AsNoTracking()
                .Where(a => accountIds.Contains(a.Id) && a.Student.SchoolId == schoolId)
                .Select(a => a.Id).ToListAsync(ct)).ToHashSet();

        var applied = new List<Guid>();
        var skipped = new List<Guid>();

        foreach (var e in entries)
        {
            if (e.AccountId is { } accId && !validAccounts.Contains(accId))
            {
                skipped.Add(e.Id); // padrón aún desfasado: se reintenta en la próxima corrida.
                continue;
            }

            if (!alreadyInCloud.Contains(e.Id))
            {
                _db.Sales.Add(new Sale
                {
                    Id = e.Id,
                    SchoolId = schoolId,
                    StudentId = e.StudentId,
                    AccountId = e.AccountId,
                    Tender = e.Tender,
                    Status = e.Status,
                    Subtotal = e.Subtotal,
                    DiscountTotal = e.DiscountTotal,
                    TaxTotal = e.TaxTotal,
                    Total = e.Total,
                    AmountTendered = e.AmountTendered,
                    CreatedAtUtc = e.CreatedAtUtc,
                    Lines = e.Lines.Select(l => new SaleLine
                    {
                        Id = l.Id,
                        ProductId = l.ProductId,
                        Description = l.Description,
                        Quantity = l.Quantity,
                        UnitPrice = l.UnitPrice,
                        Discount = l.Discount,
                        LineTotal = l.LineTotal,
                    }).ToList(),
                });

                try
                {
                    await _db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException)
                {
                    // Una venta en conflicto no debe tumbar el resto del lote (mismo patrón que
                    // PushRosterAsync); se reintenta tal cual en la próxima corrida.
                    continue;
                }
            }
            applied.Add(e.Id);
        }

        return new SalesPushResult(applied, skipped);
    }

    public async Task<IReadOnlyList<PendingOrderDto>> GetPendingOrdersAsync(Guid schoolId, CancellationToken ct = default)
    {
        var orders = await _db.PortalOrders.AsNoTracking()
            .Where(o => o.SchoolId == schoolId && !o.AppliedLocally)
            .ToListAsync(ct);
        if (orders.Count == 0)
            return Array.Empty<PendingOrderDto>();

        var orderIds = orders.Select(o => o.Id).ToList();
        var lines = await _db.PortalOrderLines.AsNoTracking()
            .Where(l => orderIds.Contains(l.OrderId))
            .ToListAsync(ct);
        var byOrder = lines.GroupBy(l => l.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        return orders.Select(o => new PendingOrderDto(
            o.Id, o.AccountId, o.Total, o.CreatedAtUtc,
            byOrder.GetValueOrDefault(o.Id, new List<PortalOrderLine>())
                .Select(l => new PendingOrderLineDto(l.ProductId, l.Description, l.Quantity, l.UnitPrice, l.LineTotal))
                .ToList()))
            .ToList();
    }

    public async Task AckOrdersAsync(Guid schoolId, IReadOnlyList<Guid> orderIds, CancellationToken ct = default)
    {
        if (orderIds.Count == 0)
            return;

        var now = _clock.UtcNow;
        // Filtrado por schoolId: un id ajeno (manipulado o de otra corrida) no toca nada aquí.
        var rows = await _db.PortalOrders
            .Where(o => o.SchoolId == schoolId && orderIds.Contains(o.Id))
            .ToListAsync(ct);
        foreach (var o in rows)
        {
            o.AppliedLocally = true;
            o.AppliedAtUtc = now;
        }
        await _db.SaveChangesAsync(ct);
    }
}
