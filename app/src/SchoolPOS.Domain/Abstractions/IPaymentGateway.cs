namespace SchoolPOS.Domain.Abstractions;

/// <summary>Datos para crear una preferencia de pago (checkout) con split de comisión.</summary>
public sealed record PaymentIntent(
    Guid TopUpId,
    decimal Amount,
    decimal CommissionAmount,
    string Currency,
    string Description);

/// <summary>Resultado de crear la preferencia: referencia de la pasarela y URL de checkout.</summary>
public sealed record PaymentPreference(string GatewayRef, string CheckoutUrl);

/// <summary>Estado de un pago reportado por la pasarela (webhook).</summary>
public enum PaymentStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
}

/// <summary>Notificación de pago ya verificada contra la pasarela (server-side).</summary>
public sealed record PaymentNotification(string GatewayRef, PaymentStatus Status);

/// <summary>
/// Datos crudos de una notificación de webhook, independientes del framework. Mercado Pago
/// requiere el cuerpo, la firma (<c>x-signature</c>), el id de solicitud (<c>x-request-id</c>) y el
/// id del recurso (<c>data.id</c>, el pago) para validar la firma HMAC.
/// </summary>
public sealed record WebhookRequest(
    string RawBody,
    string? Signature = null,
    string? RequestId = null,
    string? ResourceId = null);

/// <summary>
/// Abstracción de la pasarela de pago (Mercado Pago). El importe de comisión se envía como
/// <c>application_fee</c> del marketplace para que se separe automáticamente a la cuenta del
/// proveedor (FR-COM-2). La confirmación del pago se valida por webhook server-side, nunca por
/// la redirección del navegador (NFR-3). La implementación real (HTTP) vive en el host del portal.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Crea la preferencia de checkout con el split de comisión y devuelve la URL.</summary>
    Task<PaymentPreference> CreatePreferenceAsync(PaymentIntent intent, CancellationToken ct = default);

    /// <summary>
    /// Verifica una notificación de webhook contra la pasarela (firma + consulta del pago) y
    /// devuelve su estado real. Devuelve <c>null</c> si la notificación no es válida.
    /// </summary>
    Task<PaymentNotification?> VerifyWebhookAsync(WebhookRequest request, CancellationToken ct = default);
}
