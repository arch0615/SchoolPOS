using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Portal.Web.Infrastructure;

namespace SchoolPOS.Portal.Web.Pages.Account;

public class RegisterModel : PageModel
{
    private readonly IGuardianService _guardians;
    private readonly PortalOptions _options;

    public RegisterModel(IGuardianService guardians, PortalOptions options)
    {
        _guardians = guardians;
        _options = options;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? Error { get; set; }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "El nombre es obligatorio.")]
        public string FullName { get; set; } = string.Empty;

        [Required, EmailAddress(ErrorMessage = "Correo no válido.")]
        public string Email { get; set; } = string.Empty;

        [Required, MinLength(6, ErrorMessage = "La contraseña debe tener al menos 6 caracteres.")]
        public string Password { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            Error = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault();
            return Page();
        }

        try
        {
            var guardian = await _guardians.RegisterAsync(
                _options.SchoolId, Input.Email, Input.Password, Input.FullName);
            await PortalSignIn.SignInAsync(HttpContext, guardian);
            return RedirectToPage("/Dashboard");
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            return Page();
        }
    }
}
