using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SchoolPOS.Portal.Web.Pages.Account;

public class LogoutModel : PageModel
{
    // POST desde el botón "Salir" del encabezado.
    public Task<IActionResult> OnPostAsync() => SignOutAndRedirectAsync();

    // GET directo a /Account/Logout: también cierra la sesión antes de ir al inicio.
    public Task<IActionResult> OnGetAsync() => SignOutAndRedirectAsync();

    private async Task<IActionResult> SignOutAndRedirectAsync()
    {
        // Un solo esquema de cookie para padres y proveedores: cerrarlo borra la sesión de ambos.
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToPage("/Index");
    }
}
