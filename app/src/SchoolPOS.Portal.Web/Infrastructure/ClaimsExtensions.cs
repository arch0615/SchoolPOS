using System.Security.Claims;

namespace SchoolPOS.Portal.Web.Infrastructure;

/// <summary>Ayudas para leer la identidad del tutor autenticado desde las claims de la cookie.</summary>
public static class ClaimsExtensions
{
    public const string SchoolIdClaim = "school_id";
    public const string PortalRoleClaim = "portal_role";

    public static Guid GetGuardianId(this ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var id)
            ? id
            : throw new InvalidOperationException("Sesión sin identificador de tutor.");
    }

    public static Guid GetSchoolId(this ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(SchoolIdClaim);
        return Guid.TryParse(value, out var id) ? id : Guid.Empty;
    }
}
