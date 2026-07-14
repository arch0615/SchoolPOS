using SchoolPOS.Domain.Entities;

namespace SchoolPOS.Domain.Abstractions;

/// <summary>Resultado de iniciar una recarga: el registro creado y la URL de checkout.</summary>
public sealed record TopUpCreated(TopUp TopUp, string CheckoutUrl);

/// <summary>
/// Servicio de recargas en línea (portal). Orquesta el flujo: crear la recarga con su comisión
/// calculada y preferencia de pago, confirmar por webhook (server-side) y aplicar al libro mayor
/// de forma idempotente. El estudiante siempre recibe el 100% del monto (FR-COM-1, FR-WP-6/7).
/// </summary>
public interface ITopUpService
{
    /// <summary>
    /// Crea una recarga en estado Pendiente: calcula la comisión con la tasa de la escuela, crea
    /// la preferencia de pago (con split) y guarda la <c>gateway_ref</c>. Devuelve la URL de checkout.
    /// </summary>
    Task<TopUpCreated> CreateAsync(Guid schoolId, Guid accountId, decimal amount, CancellationToken ct = default);

    /// <summary>
    /// Confirma una recarga aprobada a partir de una notificación de webhook ya verificada
    /// server-side (NFR-3). Idempotente: si ya estaba confirmada o aplicada, no cambia nada.
    /// </summary>
    Task<TopUp> ConfirmAsync(string gatewayRef, CancellationToken ct = default);

    /// <summary>
    /// Aplica al libro mayor local una recarga confirmada (acredita el 100% al estudiante), de
    /// forma idempotente. Es el paso que ejecuta el agente de sincronización sobre la DB local.
    /// </summary>
    Task<BalanceMovement> ApplyConfirmedAsync(Guid topUpId, CancellationToken ct = default);
}
