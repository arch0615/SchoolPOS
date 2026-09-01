using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Portal.Web.Infrastructure;

namespace SchoolPOS.Portal.Web.Pages.Account;

/// <summary>
/// Solicitud de restablecimiento. Ya no pide la escuela: hacerlo obligaba al tutor a adivinar cuál
/// eligió al registrarse, y equivocarse ahí producía el mismo "no me llegó el correo" que una
/// cuenta inexistente (misma respuesta neutra, cero pista de qué pasó). Busca el correo en todas
/// las escuelas y genera un token por cada cuenta encontrada — un tutor con hijos en dos escuelas
/// puede tener una cuenta en cada una con el mismo correo.
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

    [BindProperty, Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    public bool Submitted { get; private set; }

    /// <summary>Solo desarrollo: enlaces con el token (en producción llegan por correo).</summary>
    public IReadOnlyList<string> DevResetLinks { get; private set; } = Array.Empty<string>();

    public Task OnGetAsync() => Task.CompletedTask;

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        // Errores aquí (SMTP caído, etc.) se registran pero NUNCA cambian lo que ve el usuario:
        // mostrar un mensaje distinto solo cuando el envío falla filtraría si el correo existe
        // (solo hay algo que enviar cuando sí encontró una o más cuentas reales).
        try
        {
            var matches = await _guardians.RequestPasswordResetsAsync(Email);
            Submitted = true;

            if (matches.Count > 0)
            {
                var schoolNames = (await _schools.ListAsync()).ToDictionary(s => s.Id, s => s.Name);
                var devLinks = new List<string>();

                foreach (var (schoolId, token) in matches)
                {
                    var resetLink = Url.Page("/Account/ResetPassword", pageHandler: null,
                        values: new { schoolId, email = Email.Trim().ToLowerInvariant(), token },
                        protocol: Request.Scheme)!;

                    // Con más de una cuenta (hijos en escuelas distintas), aclarar a cuál
                    // corresponde cada enlace evita que el tutor abra el que no es.
                    var schoolLine = matches.Count > 1 && schoolNames.TryGetValue(schoolId, out var name)
                        ? $"<p>Escuela: <b>{name}</b></p>"
                        : "";
                    var body =
                        $"<p>Recibimos una solicitud para restablecer tu contraseña de la Tienda Escolar.</p>" +
                        schoolLine +
                        $"<p><a href=\"{resetLink}\">Restablecer contraseña</a></p>" +
                        $"<p>Si no fuiste tú, ignora este mensaje. El enlace vence en 1 hora.</p>";
                    await _email.SendAsync(Email, "Restablece tu contraseña", body);

                    devLinks.Add(resetLink);
                }

                if (_env.IsDevelopment())
                    DevResetLinks = devLinks; // conveniencia local
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallo al procesar la solicitud de restablecimiento de contraseña.");
            Submitted = true; // misma respuesta que el camino feliz — no revela nada por el error.
        }

        return Page();
    }
}
