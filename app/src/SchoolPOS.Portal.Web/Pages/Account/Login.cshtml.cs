using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Portal.Web.Infrastructure;

namespace SchoolPOS.Portal.Web.Pages.Account;

public class LoginModel : PageModel
{
    private readonly IGuardianService _guardians;
    private readonly PortalOptions _options;

    public LoginModel(IGuardianService guardians, PortalOptions options)
    {
        _guardians = guardians;
        _options = options;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? Error { get; set; }

    public sealed class InputModel
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            Error = "Ingrese correo y contraseña.";
            return Page();
        }

        var result = await _guardians.AuthenticateAsync(_options.SchoolId, Input.Email, Input.Password);
        if (!result.Succeeded || result.Guardian is null)
        {
            Error = result.Error ?? "No se pudo iniciar sesión.";
            return Page();
        }

        await PortalSignIn.SignInAsync(HttpContext, result.Guardian);
        return RedirectToPage("/Dashboard");
    }
}
