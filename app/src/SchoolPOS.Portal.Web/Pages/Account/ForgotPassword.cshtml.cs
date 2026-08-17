using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Portal.Web.Infrastructure;

namespace SchoolPOS.Portal.Web.Pages.Account;

/// <summary>
/// Solicitud de restablecimiento. Pide la escuela junto con el correo porque la cuenta del tutor
/// se identifica por el par escuela+correo; el enlace que se envía lleva la escuela para que
/// <see cref="ResetPasswordModel"/> valide el token contra la cuenta correcta.
/// </summary>
public class ForgotPasswordModel : PageModel
{
    private readonly IGuardianService _guardians;
    private readonly IEmailSender _email;
    private readonly SchoolDirectory _schools;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<ForgotPasswordModel> _logger;

    public ForgotPasswordModel(
        IGuardianService guardians, IEmailSender email, SchoolDirectory schools, IWebHostEnvironment env,
        ILogger<ForgotPasswordModel> logger)
    {
        _guardians = guardians;
        _email = email;
        _schools = schools;
        _env = env;
        _logger = logger;
    }

    [BindProperty, Required(ErrorMessage = "Selecciona tu escuela.")]
    public Guid SchoolId { get; set; }

    [BindProperty, Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    public bool Submitted { get; private set; }
    public string? Error { get; private set; }
    public IReadOnlyList<SchoolDirectory.Option> Schools { get; private set; } =
        Array.Empty<SchoolDirectory.Option>();

    /// <summary>Solo desarrollo: enlace con el token (en producción llega por correo).</summary>
    public string? DevResetLink { get; private set; }

    public async Task OnGetAsync()
    {
        await LoadSchoolsAsync();
        if (Schools.Count == 1)
            SchoolId = Schools[0].Id;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadSchoolsAsync();

        if (!ModelState.IsValid)
            return Page();

        // Escuela inexistente: misma respuesta neutra que el resto del flujo, sin tocar la DB.
        if (!await _schools.ExistsAsync(SchoolId))
        {
            Submitted = true;
            return Page();
        }

        // Errores aquí (SMTP caído, etc.) se registran pero NUNCA cambian lo que ve el usuario:
        // mostrar un mensaje distinto solo cuando el envío falla filtraría si el correo existe
        // (solo llega a este bloque una cuenta real, ya que token es null para una inexistente).
        try
        {
            var token = await _guardians.RequestPasswordResetAsync(SchoolId, Email);
            Submitted = true;

            if (token is not null)
            {
                var resetLink = Url.Page("/Account/ResetPassword", pageHandler: null,
                    values: new { schoolId = SchoolId, email = Email.Trim().ToLowerInvariant(), token },
                    protocol: Request.Scheme)!;

                var body =
                    $"<p>Recibimos una solicitud para restablecer tu contraseña de la Tienda Escolar.</p>" +
                    $"<p><a href=\"{resetLink}\">Restablecer contraseña</a></p>" +
                    $"<p>Si no fuiste tú, ignora este mensaje. El enlace vence en 1 hora.</p>";
                await _email.SendAsync(Email, "Restablece tu contraseña", body);

                if (_env.IsDevelopment())
                    DevResetLink = resetLink; // conveniencia local
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallo al procesar la solicitud de restablecimiento de contraseña.");
            Submitted = true; // misma respuesta que el camino feliz — no revela nada por el error.
        }

        return Page();
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
