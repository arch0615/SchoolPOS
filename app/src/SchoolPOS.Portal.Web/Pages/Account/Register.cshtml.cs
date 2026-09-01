using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Portal.Web.Infrastructure;

namespace SchoolPOS.Portal.Web.Pages.Account;

/// <summary>
/// Alta del tutor. La escuela elegida aquí queda fija en la cuenta (el tutor solo puede vincular
/// alumnos de esa escuela); un tutor con hijos en dos escuelas necesita una cuenta en cada una.
/// </summary>
public class RegisterModel : PageModel
{
    private readonly IGuardianService _guardians;
    private readonly SchoolDirectory _schools;

    public RegisterModel(IGuardianService guardians, SchoolDirectory schools)
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
        [Required(ErrorMessage = "Selecciona la escuela de tu hijo.")]
        public Guid SchoolId { get; set; }

        [Required(ErrorMessage = "El nombre es obligatorio.")]
        public string FullName { get; set; } = string.Empty;

        [Required, EmailAddress(ErrorMessage = "Correo no válido.")]
        public string Email { get; set; } = string.Empty;

        [Required, MinLength(6, ErrorMessage = "La contraseña debe tener al menos 6 caracteres.")]
        public string Password { get; set; } = string.Empty;

        [Range(typeof(bool), "true", "true", ErrorMessage = "Debes aceptar los Términos y Condiciones.")]
        public bool AcceptTerms { get; set; }

        [Range(typeof(bool), "true", "true", ErrorMessage = "Debes aceptar el Aviso de Privacidad.")]
        public bool AcceptPrivacy { get; set; }

        /// <summary>Marcada por omisión: a diferencia de Términos/Privacidad, no es un consentimiento
        /// legal exigido, es una preferencia — tiene sentido partir de "sí, avísame".</summary>
        public bool AcceptNotifications { get; set; } = true;
    }

    public async Task OnGetAsync()
    {
        await LoadSchoolsAsync();
        if (Schools.Count == 1)
            Input.SchoolId = Schools[0].Id;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadSchoolsAsync();

        if (!ModelState.IsValid)
        {
            Error = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault();
            return Page();
        }

        if (!await _schools.ExistsAsync(Input.SchoolId))
        {
            Error = "Selecciona una escuela válida.";
            return Page();
        }

        try
        {
            var guardian = await _guardians.RegisterAsync(
                Input.SchoolId, Input.Email, Input.Password, Input.FullName,
                Input.AcceptTerms, Input.AcceptPrivacy, Input.AcceptNotifications);
            await PortalSignIn.SignInAsync(HttpContext, guardian);
            return RedirectToPage("/Dashboard");
        }
        catch (Exception ex)
        {
            Error = ex.Message;
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
