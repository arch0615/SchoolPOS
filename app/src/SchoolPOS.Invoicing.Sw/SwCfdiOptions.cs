namespace SchoolPOS.Invoicing.Sw;

/// <summary>
/// Configuración del PAC SW Sapien y datos fiscales del <b>emisor</b> (el proveedor). El CSD del
/// emisor se carga en la cuenta de SW; aquí van las credenciales de la API y los datos del emisor.
/// </summary>
public sealed class SwCfdiOptions
{
    /// <summary>Base de la API de SW. Sandbox: https://services.test.sw.com.mx · Prod: https://services.sw.com.mx</summary>
    public string BaseUrl { get; set; } = "https://services.test.sw.com.mx";

    /// <summary>Usuario y contraseña de SW (para obtener el token). Alternativa: usar <see cref="Token"/>.</summary>
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    /// <summary>Token persistente de SW (si se prefiere sobre user/password).</summary>
    public string? Token { get; set; }

    // --- Datos fiscales del emisor (proveedor) ---
    public string IssuerRfc { get; set; } = string.Empty;
    public string IssuerName { get; set; } = string.Empty;
    public string IssuerTaxRegime { get; set; } = "626"; // RESICO por defecto (confirmar)
    public string IssuerPostalCode { get; set; } = string.Empty;

    // --- Catálogos SAT del concepto de comisión (CONFIRMAR con contador) ---
    /// <summary>Clave de producto/servicio SAT para la comisión.</summary>
    public string ConceptSatKey { get; set; } = "84121500"; // servicios de inversión/comisión (confirmar)
    /// <summary>Clave de unidad SAT (ACT = Actividad).</summary>
    public string ConceptUnitKey { get; set; } = "ACT";
    public string PaymentForm { get; set; } = "03";   // FormaPago: transferencia
    public string PaymentMethod { get; set; } = "PUE"; // MetodoPago: pago en una exhibición
}
