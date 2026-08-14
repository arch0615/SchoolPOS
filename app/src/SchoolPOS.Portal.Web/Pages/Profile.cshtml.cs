using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Entities;
using SchoolPOS.Portal.Web.Infrastructure;

namespace SchoolPOS.Portal.Web.Pages;

[Authorize(Policy = "Guardian")]
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
        try
        {
            Me = await _guardians.GetAsync(User.GetGuardianId());
            FullName = Me?.FullName ?? string.Empty;
        }
        catch (Exception ex)
        {
            Error = $"No se pudo cargar tu perfil: {ex.Message}";
        }
        return Page();
    }

    public async Task<IActionResult> OnPostProfileAsync()
    {
        try
        {
            await _guardians.UpdateProfileAsync(User.GetGuardianId(), FullName.Trim());
            Message = "Perfil actualizado.";
        }
        catch (Exception ex)
        {
            Error = $"No se pudo actualizar el perfil: {ex.Message}";
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPasswordAsync()
    {
        try
        {
            var ok = await _guardians.ChangePasswordAsync(User.GetGuardianId(), CurrentPassword, NewPassword);
            if (ok)
                Message = "Contraseña actualizada.";
            else
                Error = "La contraseña actual es incorrecta o la nueva es muy corta (mínimo 6).";
        }
        catch (Exception ex)
        {
            Error = $"No se pudo cambiar la contraseña: {ex.Message}";
        }
        return RedirectToPage();
    }
}
