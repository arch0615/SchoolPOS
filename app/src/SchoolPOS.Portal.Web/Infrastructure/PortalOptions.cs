namespace SchoolPOS.Portal.Web.Infrastructure;

/// <summary>
/// Configuración del portal. Para desarrollo el portal es de una sola escuela (SchoolId fijo).
/// En producción multi-escuela, la escuela se resolvería por subdominio o selección al registrarse.
/// </summary>
public sealed class PortalOptions
{
    public Guid SchoolId { get; init; }

    /// <summary>Código de acceso al panel del proveedor (comisiones). Configurar por instalación.</summary>
    public string VendorAccessCode { get; init; } = "vendor-demo";

    /// <summary>Monto mínimo de una recarga (MXN).</summary>
    public decimal MinTopUp { get; init; } = 20m;

    /// <summary>Monto máximo de una recarga (MXN).</summary>
    public decimal MaxTopUp { get; init; } = 5000m;

    /// <summary>Montos sugeridos en la página de recarga (MXN).</summary>
    public decimal[] TopUpPresets { get; init; } = { 100m, 200m, 500m };

    /// <summary>Saldo por debajo del cual se avisa al tutor (MXN).</summary>
    public decimal LowBalanceThreshold { get; init; } = 50m;
}
