using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Portal.Web.Infrastructure;

namespace SchoolPOS.Portal.Web.Pages;

/// <summary>
/// Mis alumnos (FR-WP-9): una tarjeta por hijo vinculado, con su saldo y accesos directos
/// a recargar o ver movimientos. Es también donde se vincula un alumno nuevo por matrícula.
/// </summary>
[Authorize]
public class StudentsModel : PageModel
{
    private readonly IGuardianService _guardians;
    private readonly PortalOptions _options;

    public StudentsModel(IGuardianService guardians, PortalOptions options)
    {
        _guardians = guardians;
        _options = options;
    }

    public IReadOnlyList<LinkedStudent> Students { get; private set; } = Array.Empty<LinkedStudent>();

    public decimal LowBalanceThreshold => _options.LowBalanceThreshold;

    [TempData] public string? Message { get; set; }
    [TempData] public string? Error { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        Students = await _guardians.GetLinkedStudentsAsync(User.GetGuardianId());
        return Page();
    }

    public async Task<IActionResult> OnPostLinkStudentAsync(string enrollmentNo)
    {
        if (string.IsNullOrWhiteSpace(enrollmentNo))
        {
            Error = "Ingrese una matrícula.";
            return RedirectToPage();
        }

        try
        {
            await _guardians.LinkStudentByEnrollmentAsync(User.GetGuardianId(), _options.SchoolId, enrollmentNo);
            Message = $"Estudiante {enrollmentNo} vinculado.";
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        return RedirectToPage();
    }

    /// <summary>Iniciales para el avatar: primera letra del nombre y del primer apellido.</summary>
    public static string Initials(string fullName)
    {
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..1].ToUpperInvariant(),
            _ => (parts[0][..1] + parts[1][..1]).ToUpperInvariant(),
        };
    }
}
