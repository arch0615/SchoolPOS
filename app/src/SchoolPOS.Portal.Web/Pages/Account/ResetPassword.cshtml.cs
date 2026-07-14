using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Portal.Web.Infrastructure;

namespace SchoolPOS.Portal.Web.Pages.Account;

public class ResetPasswordModel : PageModel
{
    private readonly IGuardianService _guardians;
    private readonly PortalOptions _options;

    public ResetPasswordModel(IGuardianService guardians, PortalOptions options)
    {
        _guardians = guardians;
        _options = options;
    }

    [BindProperty(SupportsGet = true)] public string Email { get; set; } = string.Empty;
    [BindProperty(SupportsGet = true)] public string Token { get; set; } = string.Empty;
    [BindProperty] public string NewPassword { get; set; } = string.Empty;

    [TempData] public string? LoginMessage { get; set; }
    public string? Error { get; set; }

    public IActionResult OnGet()
    {
        if (string.IsNullOrEmpty(Email) || string.IsNullOrEmpty(Token))
            return RedirectToPage("/Account/ForgotPassword");
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var ok = await _guardians.ResetPasswordAsync(_options.SchoolId, Email, Token, NewPassword);
        if (!ok)
        {
            Error = "El enlace es inválido o venció, o la contraseña es muy corta. Solicita uno nuevo.";
            return Page();
        }

        LoginMessage = "Tu contraseña fue restablecida. Ya puedes ingresar.";
        return RedirectToPage("/Account/Login");
    }
}
