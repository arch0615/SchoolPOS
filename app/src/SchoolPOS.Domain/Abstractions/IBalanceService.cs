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
    /// Aplica al libro mayor local una recarga <b>ya confirmada</b> (por webhook) de forma
    /// idempotente: si la recarga ya fue aplicada (o su <c>gateway_ref</c> ya existe), no hace
    /// nada y devuelve el asiento existente. Acredita el 100% del monto (FR-COM-1, NFR-7).
    /// </summary>
    Task<BalanceMovement> ApplyTopUpAsync(Guid topUpId, CancellationToken ct = default);

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
