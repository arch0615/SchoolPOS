namespace SchoolPOS.Domain.Entities;

/// <summary>
/// Cuenta de pago de una escuela conectada por OAuth marketplace (Mercado Pago). Guarda el token
/// del <b>vendedor</b> (la escuela) que el proveedor usa para crear los pagos, con
/// <c>marketplace_fee</c> = comisión desviada a la cuenta del proveedor (FR-COM-2).
///
/// ⚠️ Contiene secretos (access/refresh token). Cifrar en reposo en producción y nunca exponer.
/// </summary>
public class SchoolPaymentAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SchoolId { get; set; }

    /// <summary>Pasarela, p. ej. "MercadoPago".</summary>
    public string Provider { get; set; } = "MercadoPago";

    /// <summary>Id del usuario/vendedor en la pasarela (collector).</summary>
    public string ProviderUserId { get; set; } = string.Empty;

    public string AccessToken { get; set; } = string.Empty;
    public string? RefreshToken { get; set; }

    /// <summary>Momento en que expira el access token (para refrescarlo).</summary>
    public DateTime ExpiresAtUtc { get; set; }

    public DateTime ConnectedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
