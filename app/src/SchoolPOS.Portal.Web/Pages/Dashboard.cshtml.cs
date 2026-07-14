using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Portal.Web.Infrastructure;

namespace SchoolPOS.Portal.Web.Pages;

[Authorize]
public class DashboardModel : PageModel
{
    private readonly IGuardianService _guardians;
    private readonly ITopUpService _topUps;
    private readonly PortalOptions _options;

    public DashboardModel(IGuardianService guardians, ITopUpService topUps, PortalOptions options)
    {
        _guardians = guardians;
        _topUps = topUps;
        _options = options;
    }

    public IReadOnlyList<LinkedStudent> Students { get; private set; } = Array.Empty<LinkedStudent>();
    public LinkedStudent? Selected { get; private set; }
    public IReadOnlyList<MovementRow> Movements { get; private set; } = Array.Empty<MovementRow>();

    [TempData] public string? Message { get; set; }
    [TempData] public string? Error { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid? studentId)
    {
        await LoadAsync(studentId);
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

    public async Task<IActionResult> OnPostTopUpAsync(Guid studentId, Guid accountId, decimal amount)
    {
        if (amount <= 0m)
        {
            Error = "El monto debe ser mayor a cero.";
            return RedirectToPage(new { studentId });
        }

        // Control de acceso: la cuenta debe pertenecer a un hijo del tutor.
        if (!await _guardians.OwnsStudentAsync(User.GetGuardianId(), studentId))
        {
            Error = "No tiene acceso a esa cuenta.";
            return RedirectToPage();
        }

        var created = await _topUps.CreateAsync(_options.SchoolId, accountId, amount);
        // Redirige al checkout de la pasarela (en sandbox, una página interna de simulación).
        return Redirect(created.CheckoutUrl);
    }

    private async Task LoadAsync(Guid? studentId)
    {
        var guardianId = User.GetGuardianId();
        Students = await _guardians.GetLinkedStudentsAsync(guardianId);

        Selected = studentId is { } id
            ? Students.FirstOrDefault(s => s.StudentId == id) ?? Students.FirstOrDefault()
            : Students.FirstOrDefault();

        if (Selected is not null)
            Movements = await _guardians.GetMovementsAsync(Selected.AccountId, null, null);
    }
}
