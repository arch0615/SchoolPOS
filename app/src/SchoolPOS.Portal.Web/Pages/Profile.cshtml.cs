using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Entities;
using SchoolPOS.Portal.Web.Infrastructure;

namespace SchoolPOS.Portal.Web.Pages;

[Authorize]
public class ProfileModel : PageModel
{
    private readonly IGuardianService _guardians;

    public ProfileModel(IGuardianService guardians) => _guardians = guardians;

    public Guardian? Me { get; private set; }

    [BindProperty] public string FullName { get; set; } = string.Empty;
    [BindProperty] public string CurrentPassword { get; set; } = string.Empty;
    [BindProperty] public string NewPassword { get; set; } = string.Empty;

    [TempData] public string? Message { get; set; }
    [TempData] public string? Error { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        Me = await _guardians.GetAsync(User.GetGuardianId());
        FullName = Me?.FullName ?? string.Empty;
        return Page();
    }

    public async Task<IActionResult> OnPostProfileAsync()
    {
        await _guardians.UpdateProfileAsync(User.GetGuardianId(), FullName.Trim());
        Message = "Perfil actualizado.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPasswordAsync()
    {
        var ok = await _guardians.ChangePasswordAsync(User.GetGuardianId(), CurrentPassword, NewPassword);
        if (ok)
            Message = "Contraseña actualizada.";
        else
            Error = "La contraseña actual es incorrecta o la nueva es muy corta (mínimo 6).";
        return RedirectToPage();
    }
}
