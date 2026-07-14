using Microsoft.EntityFrameworkCore;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Entities;
using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Data.Services;

/// <summary>
/// Implementación del servicio de tesorería. El arqueo compara el efectivo contado contra el
/// esperado (fondo + ingresos - egresos + ventas en efectivo de la sesión). Las sumas se calculan
/// en memoria para ser portables entre proveedores (SQLite no traduce <c>SUM(decimal)</c>).
/// </summary>
public sealed class TreasuryService : ITreasuryService
{
    private readonly SchoolDbContext _db;
    private readonly IClock _clock;

    private const int Scale = 2;
    private const MidpointRounding Rounding = MidpointRounding.AwayFromZero;

    public TreasuryService(SchoolDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public Task<CashSession> OpenSessionAsync(
        Guid schoolId, Guid operatorId, decimal openingFloat, CancellationToken ct = default)
    {
        if (openingFloat < 0m)
            throw new ArgumentOutOfRangeException(nameof(openingFloat), "El fondo inicial no puede ser negativo.");

        return _db.ExecuteAtomicAsync(async () =>
        {
            var alreadyOpen = await _db.CashSessions.AnyAsync(
                s => s.SchoolId == schoolId && s.OperatorId == operatorId && s.Status == CashSessionStatus.Open, ct);
            if (alreadyOpen)
                throw new InvalidOperationException("El operador ya tiene una sesión de caja abierta.");

            var session = new CashSession
            {
                SchoolId = schoolId,
                OperatorId = operatorId,
                Status = CashSessionStatus.Open,
                OpeningFloat = Round(openingFloat),
                OpenedAtUtc = _clock.UtcNow,
            };
            _db.CashSessions.Add(session);
            await _db.SaveChangesAsync(ct);
            return session;
        }, ct);
    }

    public Task<CashMovement> RegisterMovementAsync(
        Guid sessionId, CashMovementType type, decimal amount, string reason, Guid operatorId,
        CancellationToken ct = default)
    {
        if (amount <= 0m)
            throw new ArgumentOutOfRangeException(nameof(amount), "El importe debe ser positivo.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("El motivo es obligatorio.", nameof(reason));

        return _db.ExecuteAtomicAsync(async () =>
        {
            var session = await _db.CashSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct)
                ?? throw new InvalidOperationException($"Sesión de caja {sessionId} no encontrada.");
            if (session.Status != CashSessionStatus.Open)
                throw new InvalidOperationException("No se pueden registrar movimientos en una sesión cerrada.");

            var movement = new CashMovement
            {
                CashSessionId = sessionId,
                Type = type,
                Amount = Round(amount),
                Reason = reason,
                OperatorId = operatorId,
                CreatedAtUtc = _clock.UtcNow,
            };
            _db.CashMovements.Add(movement);
            await _db.SaveChangesAsync(ct);
            return movement;
        }, ct);
    }

    public Task<CashSession> CloseSessionAsync(Guid sessionId, decimal countedAmount, CancellationToken ct = default) =>
        _db.ExecuteAtomicAsync(async () =>
        {
            var session = await _db.CashSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct)
                ?? throw new InvalidOperationException($"Sesión de caja {sessionId} no encontrada.");
            if (session.Status != CashSessionStatus.Open)
                throw new InvalidOperationException("La sesión ya está cerrada.");

            // Sumas en memoria (portable entre proveedores).
            var movements = await _db.CashMovements.AsNoTracking()
                .Where(m => m.CashSessionId == sessionId)
                .Select(m => new { m.Type, m.Amount })
                .ToListAsync(ct);
            var incomes = movements.Where(m => m.Type == CashMovementType.Income).Sum(m => m.Amount);
            var expenses = movements.Where(m => m.Type == CashMovementType.Expense).Sum(m => m.Amount);

            var cashSalesTotals = await _db.Sales.AsNoTracking()
                .Where(s => s.CashSessionId == sessionId && s.Tender == TenderType.Cash)
                .Select(s => s.Total)
                .ToListAsync(ct);
            var cashSales = cashSalesTotals.Sum();

            var expected = Round(session.OpeningFloat + incomes - expenses + cashSales);
            session.ExpectedAmount = expected;
            session.CountedAmount = Round(countedAmount);
            session.Variance = Round(session.CountedAmount.Value - expected);
            session.Status = CashSessionStatus.Closed;
            session.ClosedAtUtc = _clock.UtcNow;

            await _db.SaveChangesAsync(ct);
            return session;
        }, ct);

    private static decimal Round(decimal value) => Math.Round(value, Scale, Rounding);
}
