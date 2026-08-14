using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SchoolPOS.Portal.Web.Pages.Account;

public class LogoutModel : PageModel
{
    private readonly ILogger<LogoutModel> _logger;

    public LogoutModel(ILogger<LogoutModel> logger) => _logger = logger;

    // POST desde el botón "Salir" del encabezado.
    public Task<IActionResult> OnPostAsync() => SignOutAndRedirectAsync();

    // GET directo a /Account/Logout: también cierra la sesión antes de ir al inicio.
    public Task<IActionResult> OnGetAsync() => SignOutAndRedirectAsync();

    private async Task<IActionResult> SignOutAndRedirectAsync()
    {
        try
        {
            // Un solo esquema de cookie para padres y proveedores: cerrarlo borra la sesión de ambos.
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
        catch (Exception ex)
        {
            // Mejor esfuerzo: si algo falla al limpiar la cookie, igual mandamos al usuario a un
            // lugar seguro en vez de mostrar una excepción sin manejar.
            _logger.LogWarning(ex, "Fallo al cerrar sesión.");
        }
        return RedirectToPage("/Index");
    }
}
