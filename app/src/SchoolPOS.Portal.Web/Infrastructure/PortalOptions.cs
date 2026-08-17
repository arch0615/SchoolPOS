namespace SchoolPOS.Portal.Web.Infrastructure;

/// <summary>
/// Configuración del portal. Es <b>multi-escuela</b>: una sola instalación atiende a todas las
/// escuelas del proveedor. La escuela no se configura aquí — el tutor la elige al registrarse o
/// ingresar (<see cref="SchoolDirectory"/>) y a partir de ahí viaja en la cookie como claim
/// <c>school_id</c>, que es lo que leen las páginas autenticadas. Deliberadamente <b>no</b> existe
/// una escuela por defecto: un respaldo global volvería a atar el portal a una sola escuela sin
/// que nadie lo note.
/// </summary>
public sealed class PortalOptions
{
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
