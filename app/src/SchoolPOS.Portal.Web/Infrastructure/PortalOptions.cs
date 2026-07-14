namespace SchoolPOS.Portal.Web.Infrastructure;

/// <summary>
/// Configuración del portal. Para desarrollo el portal es de una sola escuela (SchoolId fijo).
/// En producción multi-escuela, la escuela se resolvería por subdominio o selección al registrarse.
/// </summary>
public sealed class PortalOptions
{
    public Guid SchoolId { get; init; }
}
