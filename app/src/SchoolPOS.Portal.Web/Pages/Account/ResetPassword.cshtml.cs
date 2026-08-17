using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Portal.Web.Infrastructure;

namespace SchoolPOS.Portal.Web.Pages.Account;

/// <summary>
/// Restablecimiento con el token del correo. La escuela llega en el propio enlace (y viaja oculta
/// en el formulario): identifica junto al correo qué cuenta se está restableciendo. No es un
/// secreto — el token sigue siendo lo único que autoriza el cambio.
/// </summary>
public class ResetPasswordModel : PageModel
{
    private readonly IGuardianService _guardians;

    public ResetPasswordModel(IGuardianService guardians)
    {
        _guardians = guardians;
    }

    [BindProperty(SupportsGet = true)] public Guid SchoolId { get; set; }
    [BindProperty(SupportsGet = true)] public string Email { get; set; } = string.Empty;
    [BindProperty(SupportsGet = true)] public string Token { get; set; } = string.Empty;
    [BindProperty] public string NewPassword { get; set; } = string.Empty;

    [TempData] public string? LoginMessage { get; set; }
    public string? Error { get; set; }

    public IActionResult OnGet()
    {
        if (SchoolId == Guid.Empty || string.IsNullOrEmpty(Email) || string.IsNullOrEmpty(Token))
            return RedirectToPage("/Account/ForgotPassword");
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        try
        {
            var ok = await _guardians.ResetPasswordAsync(SchoolId, Email, Token, NewPassword);
            if (!ok)
            {
                Error = "El enlace es inválido o venció, o la contraseña es muy corta. Solicita uno nuevo.";
                return Page();
            }

            LoginMessage = "Tu contraseña fue restablecida. Ya puedes ingresar.";
            return RedirectToPage("/Account/Login");
        }
        catch (Exception ex)
        {
            Error = $"No se pudo restablecer la contraseña: {ex.Message}";
            return Page();
        }
    }
}
