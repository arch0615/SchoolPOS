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
    private readonly PortalOptions _options;

    public DashboardModel(IGuardianService guardians, PortalOptions options)
    {
        _guardians = guardians;
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
