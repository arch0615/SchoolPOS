using Microsoft.EntityFrameworkCore;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Entities;
using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Data.Services;

/// <summary>
/// Implementación de la recarga en efectivo del mostrador. Todo ocurre en una sola transacción:
/// el ingreso a la caja, el registro de la recarga y el abono al libro mayor. Si algo falla no
/// queda ni dinero registrado sin acreditar ni saldo acreditado sin dinero.
/// </summary>
public sealed class CashTopUpService : ICashTopUpService
{
    private readonly SchoolDbContext _db;
    private readonly IBalanceService _balance;
    private readonly ITreasuryService _treasury;
    private readonly IClock _clock;

    /// <summary>Prefijo de la referencia: distingue de un vistazo el efectivo de un pago en línea.</summary>
    private const string CashReferencePrefix = "EFECTIVO";

    public CashTopUpService(
        SchoolDbContext db, IBalanceService balance, ITreasuryService treasury, IClock clock)
    {
        _db = db;
        _balance = balance;
        _treasury = treasury;
        _clock = clock;
    }

    public Task<TopUp> CreateAsync(
        Guid schoolId, Guid accountId, decimal amount, Guid operatorId, Guid cashSessionId,
        CancellationToken ct = default)
    {
        if (amount <= 0m)
            throw new ArgumentOutOfRangeException(nameof(amount), "El monto de la recarga debe ser positivo.");
        if (cashSessionId == Guid.Empty)
            throw new InvalidOperationException(
                "Abra su caja antes de recibir efectivo: la recarga tiene que quedar registrada en el arqueo.");

        return _db.ExecuteAtomicAsync(async () =>
        {
            var accountExists = await _db.Accounts.AnyAsync(a => a.Id == accountId, ct);
            if (!accountExists)
                throw new InvalidOperationException("La cuenta del alumno no existe.");

            var now = _clock.UtcNow;

            // Referencia única: reutiliza el mismo dedupe por GatewayRef que protege a las recargas
            // en línea, de modo que un doble clic no puede acreditar dos veces.
            var reference = $"{CashReferencePrefix}:{Guid.NewGuid():N}";

            var topUp = new TopUp
            {
                SchoolId = schoolId,
                AccountId = accountId,
                Amount = amount,
                Origin = TopUpOrigin.Cash,
                // Sin comisión: el proveedor no interviene en este cobro (no hay split que hacer).
                CommissionRate = 0m,
                CommissionAmount = 0m,
                GatewayRef = reference,
                Status = TopUpStatus.Confirmed,
                CreatedAtUtc = now,
            };
            _db.TopUps.Add(topUp);
            await _db.SaveChangesAsync(ct);

            // El efectivo entra al cajón: se asienta como ingreso de la caja del operador.
            await _treasury.RegisterMovementAsync(
                cashSessionId, CashMovementType.Income, amount,
                $"Recarga de saldo {reference}", operatorId, ct);

            // Abono al libro mayor por la vía idempotente de siempre (marca AppliedLocally).
            await _balance.ApplyTopUpAsync(topUp.Id, ct);

            return topUp;
        }, ct);
    }
}
