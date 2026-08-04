using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SchoolPOS.Data;
using SchoolPOS.Domain.Abstractions;
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
    private readonly PortalOptions _options;

    public LoginModel(IAuthService auth, SchoolDbContext db, PortalOptions options)
    {
        _auth = auth;
        _db = db;
        _options = options;
    }

    [BindProperty] public Guid SchoolId { get; set; }
    [BindProperty] public string Username { get; set; } = string.Empty;
    [BindProperty] public string Password { get; set; } = string.Empty;
    public string? Error { get; set; }
    public IReadOnlyList<SchoolOption> Schools { get; private set; } = Array.Empty<SchoolOption>();

    public async Task OnGetAsync()
    {
        await LoadSchoolsAsync();
        // Preselecciona la escuela configurada del portal, o la primera disponible.
        SchoolId = Schools.Any(s => s.Id == _options.SchoolId)
            ? _options.SchoolId
            : Schools.FirstOrDefault()?.Id ?? Guid.Empty;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadSchoolsAsync();

        if (SchoolId == Guid.Empty || Schools.All(s => s.Id != SchoolId))
        {
            Error = "Selecciona una escuela válida.";
            return Page();
        }

        var result = await _auth.AuthenticateAsync(SchoolId, Username.Trim(), Password);
        if (!result.Succeeded || result.User is null)
        {
            Error = "Usuario o contraseña incorrectos.";
            return Page();
        }

        await PortalSignIn.SignInSchoolAsync(HttpContext, result.User);
        return RedirectToPage("/School/Store");
    }

    private async Task LoadSchoolsAsync() =>
        Schools = await _db.Schools
            .OrderBy(s => s.Name)
            .Select(s => new SchoolOption(s.Id, s.Name))
            .ToListAsync();

    public sealed record SchoolOption(Guid Id, string Name);
}
