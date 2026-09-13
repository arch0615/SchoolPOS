using SchoolPOS.Domain.Entities;

namespace SchoolPOS.Domain.Abstractions;

/// <summary>
/// Servicio central de saldo (NFR-1). Toda mutación del saldo ocurre en una transacción
/// atómica junto con su asiento inmutable en el libro mayor, de modo que el saldo y la suma
/// de movimientos siempre reconcilian. Previene sobregiro y doble gasto bajo concurrencia.
/// </summary>
public interface IBalanceService
{
    /// <summary>
    /// Aplica al libro mayor <b>de esta base de datos</b> una recarga ya confirmada, de forma
    /// idempotente: si ya fue aplicada aquí (o su <c>gateway_ref</c> ya tiene asiento), no hace
    /// nada y devuelve el asiento existente. Acredita el 100% del monto (FR-COM-1, NFR-7) y marca
    /// <c>AppliedLocally</c>. <b>Uso exclusivo del Sync Agent sobre la DB local de una escuela</b>
    /// — llamarlo desde el portal (nube) marcaría la recarga como entregada antes de que ninguna
    /// caja la haya recibido, y ninguna caja volvería a verla nunca (así se reprodujo el bug real:
    /// el webhook de pagos llamaba a este método contra la base central). Para acreditar el saldo
    /// en la nube al confirmar el pago, usa <see cref="CreditConfirmedTopUpAsync"/>.
    /// </summary>
    Task<BalanceMovement> ApplyTopUpAsync(Guid topUpId, CancellationToken ct = default);

    /// <summary>
    /// Acredita en la nube el 100% de una recarga <b>ya confirmada</b> por webhook, para que el
    /// tutor vea su saldo actualizado de inmediato — sin marcarla como entregada a ninguna caja.
    /// Idempotente por <c>gateway_ref</c> (los webhooks de la pasarela pueden reintentar la
    /// entrega). La entrega real a cada caja sigue el camino normal de sincronización
    /// (<c>PullTopUpsAsync</c> → <see cref="ApplyTopUpAsync"/> en la DB local → acuse).
    /// </summary>
    Task<BalanceMovement> CreditConfirmedTopUpAsync(Guid topUpId, CancellationToken ct = default);

    /// <summary>
    /// Cobra una venta contra el saldo (cargo). Rechaza con
    /// <see cref="Exceptions.InsufficientBalanceException"/> si no alcanza el saldo disponible
    /// más el sobregiro permitido (FR-SAL-2).
    /// </summary>
    Task<BalanceMovement> ChargeSaleAsync(
        Guid accountId, decimal amount, string reference, Guid operatorId, CancellationToken ct = default);

    /// <summary>Reintegra saldo por una devolución total/parcial (abono), con traza (FR-SAL-5).</summary>
    Task<BalanceMovement> RefundAsync(
        Guid accountId, decimal amount, string reference, Guid operatorId, CancellationToken ct = default);

    /// <summary>
    /// Ajuste manual auditado de saldo (positivo o negativo), FR-ADM-2. El importe con signo
    /// indica la dirección. Escribe también en la bitácora (responsabilidad del llamador o del
    /// propio servicio según implementación).
    /// </summary>
    Task<BalanceMovement> AdjustAsync(
        Guid accountId, decimal amount, string reason, Guid operatorId, CancellationToken ct = default);
}
