using Microsoft.EntityFrameworkCore;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Entities;
using SchoolPOS.Domain.Enums;
using SchoolPOS.Domain.Exceptions;

namespace SchoolPOS.Data.Services;

/// <summary>
/// Implementación transaccional del libro mayor de saldo (NFR-1). Los cargos usan un
/// <c>UPDATE</c> condicional a nivel de base de datos (<c>WHERE Balance + OverdraftLimit &gt;=
/// importe</c>), atómico por fila, por lo que dos cargos concurrentes nunca sobregiran ni
/// gastan doble. Cada mutación de saldo se persiste junto con su asiento inmutable dentro de
/// la misma transacción; por construcción <c>SUM(Amount) == Account.Balance</c> siempre reconcilia.
/// </summary>
public sealed class BalanceService : IBalanceService
{
    private readonly SchoolDbContext _db;
    private readonly IClock _clock;

    private const int Scale = 2;
    private const MidpointRounding Rounding = MidpointRounding.AwayFromZero;

    public BalanceService(SchoolDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<BalanceMovement> ApplyTopUpAsync(Guid topUpId, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var topUp = await _db.TopUps.FirstOrDefaultAsync(t => t.Id == topUpId, ct)
            ?? throw new InvalidOperationException($"Recarga {topUpId} no encontrada.");

        // Idempotencia: si ya fue aplicada, no hacer nada y devolver el asiento existente (NFR-7).
        if (topUp.AppliedLocally)
        {
            var existing = await _db.BalanceMovements.AsNoTracking()
                .FirstOrDefaultAsync(m => m.Type == MovementType.TopUp && m.Reference == topUp.GatewayRef, ct);
            await tx.CommitAsync(ct);
            return existing
                ?? throw new InvalidOperationException(
                    $"Inconsistencia: la recarga {topUpId} figura aplicada pero sin asiento.");
        }

        var amount = Math.Round(topUp.Amount, Scale, Rounding); // 100% al estudiante (FR-COM-1)
        var now = _clock.UtcNow;

        await _db.Accounts
            .Where(a => a.Id == topUp.AccountId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Balance, a => a.Balance + amount)
                .SetProperty(a => a.UpdatedAtUtc, now), ct);

        var newBalance = await ReadBalanceAsync(topUp.AccountId, ct);

        var movement = new BalanceMovement
        {
            AccountId = topUp.AccountId,
            Type = MovementType.TopUp,
            Amount = amount,
            BalanceAfter = newBalance,
            Reference = topUp.GatewayRef,
            OperatorId = null, // recarga del portal: sin operador
            CreatedAtUtc = now,
        };
        _db.BalanceMovements.Add(movement);

        topUp.AppliedLocally = true;
        topUp.Status = TopUpStatus.Applied;
        topUp.AppliedAtUtc = now;

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return movement;
    }

    public Task<BalanceMovement> ChargeSaleAsync(
        Guid accountId, decimal amount, string reference, Guid operatorId, CancellationToken ct = default)
        => ApplyGuardedDebitAsync(accountId, amount, MovementType.Sale, reference, operatorId, ct);

    public Task<BalanceMovement> RefundAsync(
        Guid accountId, decimal amount, string reference, Guid operatorId, CancellationToken ct = default)
        => ApplyCreditAsync(accountId, amount, MovementType.Refund, reference, operatorId, ct);

    public async Task<BalanceMovement> AdjustAsync(
        Guid accountId, decimal amount, string reason, Guid operatorId, CancellationToken ct = default)
    {
        // Ajuste manual auditado: importe con signo, sin verificación de suficiencia (autoridad admin).
        if (amount == 0m)
            throw new ArgumentException("El ajuste no puede ser cero.", nameof(amount));

        var signed = Math.Round(amount, Scale, Rounding);
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var now = _clock.UtcNow;

        await _db.Accounts
            .Where(a => a.Id == accountId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Balance, a => a.Balance + signed)
                .SetProperty(a => a.UpdatedAtUtc, now), ct);

        var movement = await AppendMovementAsync(
            accountId, MovementType.Adjustment, signed, reason, operatorId, now, ct);
        await tx.CommitAsync(ct);
        return movement;
    }

    // ---- helpers ----

    private async Task<BalanceMovement> ApplyGuardedDebitAsync(
        Guid accountId, decimal amount, MovementType type, string reference, Guid operatorId, CancellationToken ct)
    {
        RequirePositive(amount);
        var debit = Math.Round(amount, Scale, Rounding);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // UPDATE atómico condicional: solo descuenta si alcanza saldo + sobregiro permitido.
        var affected = await _db.Accounts
            .Where(a => a.Id == accountId && a.Balance + a.OverdraftLimit >= debit)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Balance, a => a.Balance - debit)
                .SetProperty(a => a.UpdatedAtUtc, _clock.UtcNow), ct);

        if (affected == 0)
        {
            var account = await _db.Accounts.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == accountId, ct)
                ?? throw new InvalidOperationException($"Cuenta {accountId} no encontrada.");
            throw new InsufficientBalanceException(accountId, debit, account.Balance + account.OverdraftLimit);
        }

        var movement = await AppendMovementAsync(
            accountId, type, -debit, reference, operatorId, _clock.UtcNow, ct);
        await tx.CommitAsync(ct);
        return movement;
    }

    private async Task<BalanceMovement> ApplyCreditAsync(
        Guid accountId, decimal amount, MovementType type, string reference, Guid operatorId, CancellationToken ct)
    {
        RequirePositive(amount);
        var credit = Math.Round(amount, Scale, Rounding);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var now = _clock.UtcNow;

        var affected = await _db.Accounts
            .Where(a => a.Id == accountId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Balance, a => a.Balance + credit)
                .SetProperty(a => a.UpdatedAtUtc, now), ct);

        if (affected == 0)
            throw new InvalidOperationException($"Cuenta {accountId} no encontrada.");

        var movement = await AppendMovementAsync(accountId, type, credit, reference, operatorId, now, ct);
        await tx.CommitAsync(ct);
        return movement;
    }

    /// <summary>Inserta el asiento inmutable con la instantánea del saldo resultante.</summary>
    private async Task<BalanceMovement> AppendMovementAsync(
        Guid accountId, MovementType type, decimal signedAmount, string? reference,
        Guid? operatorId, DateTime now, CancellationToken ct)
    {
        var newBalance = await ReadBalanceAsync(accountId, ct);
        var movement = new BalanceMovement
        {
            AccountId = accountId,
            Type = type,
            Amount = signedAmount,
            BalanceAfter = newBalance,
            Reference = reference,
            OperatorId = operatorId,
            CreatedAtUtc = now,
        };
        _db.BalanceMovements.Add(movement);
        await _db.SaveChangesAsync(ct);
        return movement;
    }

    private Task<decimal> ReadBalanceAsync(Guid accountId, CancellationToken ct) =>
        _db.Accounts.AsNoTracking().Where(a => a.Id == accountId).Select(a => a.Balance).FirstAsync(ct);

    private static void RequirePositive(decimal amount)
    {
        if (amount <= 0m)
            throw new ArgumentOutOfRangeException(nameof(amount), "El importe debe ser positivo.");
    }
}
