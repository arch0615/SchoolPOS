using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Portal.Web.Infrastructure;

namespace SchoolPOS.Portal.Web.Pages;

[Authorize]
public class HistoryModel : PageModel
{
    private readonly IGuardianService _guardians;

    public HistoryModel(IGuardianService guardians) => _guardians = guardians;

    public IReadOnlyList<LinkedStudent> Students { get; private set; } = Array.Empty<LinkedStudent>();
    public LinkedStudent? Selected { get; private set; }
    public IReadOnlyList<MovementRow> Movements { get; private set; } = Array.Empty<MovementRow>();

    [BindProperty(SupportsGet = true)] public Guid? StudentId { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var guardianId = User.GetGuardianId();
        Students = await _guardians.GetLinkedStudentsAsync(guardianId);
        Selected = StudentId is { } id
            ? Students.FirstOrDefault(s => s.StudentId == id) ?? Students.FirstOrDefault()
            : Students.FirstOrDefault();

        if (Selected is not null)
        {
            // 'To' inclusivo hasta el final del día.
            var toUtc = To?.Date.AddDays(1).AddTicks(-1);
            Movements = await _guardians.GetMovementsAsync(Selected.AccountId, From?.Date, toUtc);
        }
        return Page();
    }
}
