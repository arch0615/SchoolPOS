namespace SchoolPOS.Payments.MercadoPago;

/// <summary>
/// Configuración de Mercado Pago por escuela. En el modelo marketplace, cada escuela (vendedor)
/// conecta su cuenta por OAuth a la app del proveedor; el <see cref="AccessToken"/> es el token del
/// vendedor y <c>marketplace_fee</c> desvía la comisión a la cuenta del proveedor (split, FR-COM-2).
/// </summary>
public sealed class MercadoPagoOptions
{
    /// <summary>Access token del vendedor (escuela) obtenido por OAuth marketplace.</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>Clave secreta para validar la firma de los webhooks (x-signature).</summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>URL pública que recibe las notificaciones (webhook) del pago.</summary>
    public string NotificationUrl { get; set; } = string.Empty;

    public string SuccessUrl { get; set; } = string.Empty;
    public string FailureUrl { get; set; } = string.Empty;
    public string PendingUrl { get; set; } = string.Empty;

    /// <summary>Base de la API de Mercado Pago.</summary>
    public string BaseUrl { get; set; } = "https://api.mercadopago.com";
}
