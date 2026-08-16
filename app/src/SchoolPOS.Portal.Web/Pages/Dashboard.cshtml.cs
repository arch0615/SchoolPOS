using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Enums;
using SchoolPOS.Portal.Web.Infrastructure;

namespace SchoolPOS.Portal.Web.Pages;

[Authorize(Policy = "Guardian")]
public class DashboardModel : PageModel
{
    private readonly IGuardianService _guardians;

    public DashboardModel(IGuardianService guardians) => _guardians = guardians;

    public IReadOnlyList<LinkedStudent> Students { get; private set; } = Array.Empty<LinkedStudent>();
    public LinkedStudent? Selected { get; private set; }

    /// <summary>Página actual, ya filtrada por <see cref="Kind"/> — lo que se dibuja en la lista.</summary>
    public IReadOnlyList<MovementRow> Movements { get; private set; } = Array.Empty<MovementRow>();

    /// <summary>Todos los movimientos sin filtrar por tipo ni paginar — para "Gastado reciente" /
    /// "Última recarga", que deben reflejar la actividad real y no la pestaña/página activa.</summary>
    public IReadOnlyList<MovementRow> RecentSummary { get; private set; } = Array.Empty<MovementRow>();

    /// <summary>Movimientos que coinciden con <see cref="Kind"/>, antes de paginar.</summary>
    public int FilteredCount { get; private set; }

    public int TotalPages { get; private set; } = 1;

    [BindProperty(SupportsGet = true)] public MovementFilter Kind { get; set; } = MovementFilter.All;
    // Llamado "PageNumber", no "Page": PageModel ya expone un método Page() para renderizar.
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public int PageSize { get; set; } = 5;

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
        // Mismos tamaños permitidos que /Transactions; cualquier otro valor (manipulado por URL)
        // cae al valor por defecto en vez de permitir páginas arbitrariamente grandes.
        if (!TransactionsModel.PageSizeOptions.Contains(PageSize))
            PageSize = 5;
        if (PageNumber < 1)
            PageNumber = 1;

        var guardianId = User.GetGuardianId();
        Students = await _guardians.GetLinkedStudentsAsync(guardianId);

        Selected = studentId is { } id
            ? Students.FirstOrDefault(s => s.StudentId == id) ?? Students.FirstOrDefault()
            : Students.FirstOrDefault();

        if (Selected is null)
            return;

        var all = await _guardians.GetMovementsAsync(Selected.AccountId, null, null);
        RecentSummary = all;

        var filtered = Kind switch
        {
            MovementFilter.TopUps => all.Where(m => m.Type == nameof(MovementType.TopUp)).ToList(),
            MovementFilter.Purchases => all.Where(m => m.Type == nameof(MovementType.Sale)).ToList(),
            _ => all,
        };

        FilteredCount = filtered.Count;
        TotalPages = FilteredCount == 0 ? 1 : (int)Math.Ceiling(FilteredCount / (double)PageSize);
        if (PageNumber > TotalPages)
            PageNumber = TotalPages;

        Movements = filtered.Skip((PageNumber - 1) * PageSize).Take(PageSize).ToList();
    }
}
