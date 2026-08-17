using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Portal.Web.Infrastructure;

namespace SchoolPOS.Portal.Web.Pages.Account;

/// <summary>
/// Acceso del tutor. El correo identifica al tutor <b>dentro de una escuela</b> (el índice único es
/// SchoolId+Email), así que la escuela se elige aquí y no puede deducirse del correo: el mismo
/// correo puede tener cuenta en dos escuelas distintas.
/// </summary>
public class LoginModel : PageModel
{
    private readonly IGuardianService _guardians;
    private readonly SchoolDirectory _schools;

    public LoginModel(IGuardianService guardians, SchoolDirectory schools)
    {
        _guardians = guardians;
        _schools = schools;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? Error { get; set; }
    public IReadOnlyList<SchoolDirectory.Option> Schools { get; private set; } =
        Array.Empty<SchoolDirectory.Option>();

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Selecciona tu escuela.")]
        public Guid SchoolId { get; set; }

        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;
    }

    public async Task OnGetAsync()
    {
        await LoadSchoolsAsync();
        if (Schools.Count == 1)
            Input.SchoolId = Schools[0].Id; // con una sola escuela, no hay nada que elegir.
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadSchoolsAsync();

        if (!ModelState.IsValid)
        {
            Error = "Selecciona tu escuela e ingresa correo y contraseña.";
            return Page();
        }

        if (!await _schools.ExistsAsync(Input.SchoolId))
        {
            Error = "Selecciona una escuela válida.";
            return Page();
        }

        try
        {
            var result = await _guardians.AuthenticateAsync(Input.SchoolId, Input.Email, Input.Password);
            if (!result.Succeeded || result.Guardian is null)
            {
                Error = result.Error ?? "No se pudo iniciar sesión.";
                return Page();
            }

            await PortalSignIn.SignInAsync(HttpContext, result.Guardian);
            return RedirectToPage("/Dashboard");
        }
        catch (Exception ex)
        {
            Error = $"No se pudo iniciar sesión: {ex.Message}";
            return Page();
        }
    }

    private async Task LoadSchoolsAsync()
    {
        try
        {
            Schools = await _schools.ListAsync();
        }
        catch (Exception)
        {
            Schools = Array.Empty<SchoolDirectory.Option>();
            Error = "No se pudo cargar el listado de escuelas. Intenta de nuevo en un momento.";
        }
    }
}
