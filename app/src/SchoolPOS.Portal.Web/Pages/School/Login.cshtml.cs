using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SchoolPOS.Data;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Enums;
using SchoolPOS.Portal.Web.Infrastructure;

namespace SchoolPOS.Portal.Web.Pages.School;

/// <summary>
/// Acceso de la tienda escolar: el operador elige su escuela y entra con su usuario/contraseña del
/// POS; solo verá el panel de esa escuela. Reutiliza la autenticación de operadores (IAuthService).
/// </summary>
public class LoginModel : PageModel
{
    private readonly IAuthService _auth;
    private readonly SchoolDbContext _db;

    public LoginModel(IAuthService auth, SchoolDbContext db)
    {
        _auth = auth;
        _db = db;
    }

    [BindProperty] public Guid SchoolId { get; set; }
    [BindProperty] public string Username { get; set; } = string.Empty;
    [BindProperty] public string Password { get; set; } = string.Empty;
    public string? Error { get; set; }
    public IReadOnlyList<SchoolOption> Schools { get; private set; } = Array.Empty<SchoolOption>();

    public async Task OnGetAsync()
    {
        try
        {
            await LoadSchoolsAsync();
            // Con una sola escuela dada de alta no hay nada que elegir; con varias, que elija.
            SchoolId = Schools.Count == 1 ? Schools[0].Id : Guid.Empty;
        }
        catch (Exception ex)
        {
            Error = $"No se pudo cargar el listado de escuelas: {ex.Message}";
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        try
        {
            await LoadSchoolsAsync();

            if (SchoolId == Guid.Empty || Schools.All(s => s.Id != SchoolId))
            {
                Error = "Selecciona una escuela válida.";
                return Page();
            }

            var user = Username.Trim();
            if (!user.Contains('@'))
            {
                Error = "El usuario debe ser un correo electrónico (incluir @).";
                return Page();
            }

            var result = await _auth.AuthenticateAsync(SchoolId, user, Password);
            if (!result.Succeeded || result.User is null)
            {
                // El bloqueo sí se explica: con el mensaje genérico, un operador con la contraseña
                // correcta la reintenta creyéndose equivocado y solo alarga el bloqueo. El resto de
                // fallas siguen siendo genéricas (no revelan si el usuario existe).
                Error = result.IsLockedOut
                    ? result.Error
                    : "Usuario o contraseña incorrectos.";
                return Page();
            }

            await PortalSignIn.SignInSchoolAsync(HttpContext, result.User);

            // Cada rol aterriza en la página que sí puede ver (RBAC).
            var target = result.User.Role switch
            {
                UserRole.Admin => "/School/Store",
                UserRole.Warehouse => "/School/Inventory",
                _ => "/School/Sales",
            };
            return RedirectToPage(target);
        }
        catch (Exception ex)
        {
            Error = $"No se pudo iniciar sesión: {ex.Message}";
            return Page();
        }
    }

    private async Task LoadSchoolsAsync() =>
        Schools = await _db.Schools
            .OrderBy(s => s.Name)
            .Select(s => new SchoolOption(s.Id, s.Name))
            .ToListAsync();

    public sealed record SchoolOption(Guid Id, string Name);
}
