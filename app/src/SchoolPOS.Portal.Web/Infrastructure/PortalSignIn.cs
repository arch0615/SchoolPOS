using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using SchoolPOS.Domain.Entities;

namespace SchoolPOS.Portal.Web.Infrastructure;

/// <summary>Emite la cookie de sesión del tutor con sus claims.</summary>
public static class PortalSignIn
{
    public static Task SignInAsync(HttpContext http, Guardian guardian)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, guardian.Id.ToString()),
            new(ClaimTypes.Name, string.IsNullOrWhiteSpace(guardian.FullName) ? guardian.Email : guardian.FullName),
            new(ClaimTypes.Email, guardian.Email),
            new(ClaimsExtensions.SchoolIdClaim, guardian.SchoolId.ToString()),
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        return http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    }

    /// <summary>Emite la cookie del proveedor (acceso al panel de comisiones).</summary>
    public static Task SignInVendorAsync(HttpContext http)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, "Proveedor"),
            new(ClaimsExtensions.PortalRoleClaim, "vendor"),
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        return http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    }
}
