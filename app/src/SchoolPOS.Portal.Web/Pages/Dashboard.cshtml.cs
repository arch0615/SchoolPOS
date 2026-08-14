using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Portal.Web.Infrastructure;

namespace SchoolPOS.Portal.Web.Pages;

[Authorize(Policy = "Guardian")]
public class DashboardModel : PageModel
{
    private readonly IGuardianService _guardians;

    public DashboardModel(IGuardianService guardians) => _guardians = guardians;

    public IReadOnlyList<LinkedStudent> Students { get; private set; } = Array.Empty<LinkedStudent>();
    public LinkedStudent? Selected { get; private set; }
    public IReadOnlyList<MovementRow> Movements { get; private set; } = Array.Empty<MovementRow>();

    [TempData] public string? Message { get; set; }
    [TempData] public string? Error { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid? studentId)
    {
        try
        {
            await LoadAsync(studentId);
        }
        catch (Exception ex)
        {
            Error = $"No se pudo cargar tu panel: {ex.Message}";
        }
        return Page();
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
