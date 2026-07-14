using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Portal.Web.Infrastructure;

namespace SchoolPOS.Portal.Web.Pages.Account;

public class ForgotPasswordModel : PageModel
{
    private readonly IGuardianService _guardians;
    private readonly PortalOptions _options;

    public ForgotPasswordModel(IGuardianService guardians, PortalOptions options)
    {
        _guardians = guardians;
        _options = options;
    }

    [BindProperty, Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    public bool Submitted { get; private set; }

    /// <summary>Solo desarrollo: enlace con el token (en producción se envía por correo).</summary>
    public string? DevResetLink { get; private set; }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        var token = await _guardians.RequestPasswordResetAsync(_options.SchoolId, Email);
        Submitted = true;
        if (token is not null)
            DevResetLink = Url.Page("/Account/ResetPassword",
                new { email = Email.Trim().ToLowerInvariant(), token });
        return Page();
    }
}
