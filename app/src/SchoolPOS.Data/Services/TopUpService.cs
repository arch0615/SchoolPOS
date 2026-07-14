using Microsoft.EntityFrameworkCore;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Common;
using SchoolPOS.Domain.Entities;
using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Data.Services;

/// <summary>
/// Implementación del flujo de recargas. Calcula la comisión con la tasa de la escuela, crea la
/// preferencia de pago con split vía <see cref="IPaymentGateway"/>, y confirma/aplica de forma
/// idempotente. El estudiante siempre se acredita el 100%; la comisión se separa a la cuenta del
/// proveedor en la pasarela. La aplicación al libro mayor delega en <see cref="IBalanceService"/>.
/// </summary>
public sealed class TopUpService : ITopUpService
{
    private readonly SchoolDbContext _db;
    private readonly IPaymentGateway _gateway;
    private readonly IBalanceService _balance;
    private readonly IClock _clock;

    public TopUpService(SchoolDbContext db, IPaymentGateway gateway, IBalanceService balance, IClock clock)
    {
        _db = db;
        _gateway = gateway;
        _balance = balance;
        _clock = clock;
    }

    public async Task<TopUpCreated> CreateAsync(
        Guid schoolId, Guid accountId, decimal amount, CancellationToken ct = default)
    {
        if (amount <= 0m)
            throw new ArgumentOutOfRangeException(nameof(amount), "El monto de recarga debe ser positivo.");

        var school = await _db.Schools.AsNoTracking().FirstOrDefaultAsync(s => s.Id == schoolId, ct)
            ?? throw new InvalidOperationException($"Escuela {schoolId} no encontrada.");
        var accountExists = await _db.Accounts.AnyAsync(a => a.Id == accountId, ct);
        if (!accountExists)
            throw new InvalidOperationException($"Cuenta {accountId} no encontrada.");

        var commission = CommissionCalculator.Compute(amount, school.CommissionRate);

        var topUp = new TopUp
        {
            SchoolId = schoolId,
            AccountId = accountId,
            Amount = amount,               // 100% al estudiante
            CommissionRate = school.CommissionRate,
            CommissionAmount = commission,
            Status = TopUpStatus.Pending,
            CreatedAtUtc = _clock.UtcNow,
        };

        // Llamada externa a la pasarela FUERA de cualquier transacción de DB.
        var preference = await _gateway.CreatePreferenceAsync(new PaymentIntent(
            topUp.Id, amount, commission, school.Currency, $"Recarga de saldo {amount:0.00} {school.Currency}"), ct);

        topUp.GatewayRef = preference.GatewayRef;
        _db.TopUps.Add(topUp);
        await _db.SaveChangesAsync(ct);

        return new TopUpCreated(topUp, preference.CheckoutUrl);
    }

    public Task<TopUp> ConfirmAsync(string gatewayRef, CancellationToken ct = default) =>
        _db.ExecuteAtomicAsync(async () =>
        {
            var topUp = await _db.TopUps.FirstOrDefaultAsync(t => t.GatewayRef == gatewayRef, ct)
                ?? throw new InvalidOperationException($"Recarga con referencia {gatewayRef} no encontrada.");

            // Idempotente: si ya está confirmada o aplicada, no cambia nada.
            if (topUp.Status is TopUpStatus.Confirmed or TopUpStatus.Applied)
                return topUp;

            topUp.Status = TopUpStatus.Confirmed;
            await _db.SaveChangesAsync(ct);
            return topUp;
        }, ct);

    public Task<BalanceMovement> ApplyConfirmedAsync(Guid topUpId, CancellationToken ct = default) =>
        _db.ExecuteAtomicAsync(async () =>
        {
            var status = await _db.TopUps.AsNoTracking()
                .Where(t => t.Id == topUpId).Select(t => (TopUpStatus?)t.Status).FirstOrDefaultAsync(ct)
                ?? throw new InvalidOperationException($"Recarga {topUpId} no encontrada.");

            if (status is not (TopUpStatus.Confirmed or TopUpStatus.Applied))
                throw new InvalidOperationException(
                    $"Solo se puede aplicar una recarga confirmada (estado actual: {status}).");

            // Acredita el 100% al libro mayor local; idempotente y marca Applied.
            return await _balance.ApplyTopUpAsync(topUpId, ct);
        }, ct);
}
