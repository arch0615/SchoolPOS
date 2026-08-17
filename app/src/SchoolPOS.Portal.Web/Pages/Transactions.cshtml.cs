using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Common;
using SchoolPOS.Domain.Enums;
using SchoolPOS.Portal.Web.Infrastructure;

namespace SchoolPOS.Portal.Web.Pages;

/// <summary>Qué movimientos mostrar (FR-WP-8).</summary>
public enum MovementFilter
{
    /// <summary>Recargas y consumos.</summary>
    All,
    /// <summary>Sólo abonos: recargas en línea.</summary>
    TopUps,
    /// <summary>Sólo cargos: compras en la tienda.</summary>
    Purchases,
}

[Authorize(Policy = "Guardian")]
public class TransactionsModel : PageModel
{
    private readonly IGuardianService _guardians;

    public TransactionsModel(IGuardianService guardians) => _guardians = guardians;

    public IReadOnlyList<LinkedStudent> Students { get; private set; } = Array.Empty<LinkedStudent>();
    public LinkedStudent? Selected { get; private set; }

    /// <summary>Sólo la página actual — lo que se dibuja en la lista.</summary>
    public IReadOnlyList<MovementRow> Movements { get; private set; } = Array.Empty<MovementRow>();

    /// <summary>Todo lo que coincide con el filtro, sin paginar — para el CSV completo.</summary>
    public IReadOnlyList<MovementRow> AllFilteredMovements { get; private set; } = Array.Empty<MovementRow>();

    /// <summary>Suma de los abonos del rango mostrado.</summary>
    public decimal TotalIn { get; private set; }

    /// <summary>Suma de los cargos del rango mostrado, en positivo.</summary>
    public decimal TotalOut { get; private set; }

    /// <summary>Tamaños de página permitidos (FR-WP-8 paginación).</summary>
    public static readonly int[] PageSizeOptions = { 5, 10, 20 };

    /// <summary>Movimientos que coinciden con el filtro, antes de paginar.</summary>
    public int FilteredCount { get; private set; }

    public int TotalPages { get; private set; } = 1;

    [BindProperty(SupportsGet = true)] public Guid? StudentId { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }
    [BindProperty(SupportsGet = true)] public MovementFilter Kind { get; set; } = MovementFilter.All;
    // Llamado "PageNumber", no "Page": PageModel ya expone un método Page() para renderizar.
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public int PageSize { get; set; } = 10;

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        // Sólo se aceptan los tamaños ofrecidos en la interfaz; cualquier otro valor (manipulado
        // por URL) cae al valor por defecto en vez de permitir páginas arbitrariamente grandes.
        if (!PageSizeOptions.Contains(PageSize))
            PageSize = 10;
        if (PageNumber < 1)
            PageNumber = 1;

        try
        {
            var guardianId = User.GetGuardianId();
            Students = await _guardians.GetLinkedStudentsAsync(guardianId);
            Selected = StudentId is { } id
                ? Students.FirstOrDefault(s => s.StudentId == id) ?? Students.FirstOrDefault()
                : Students.FirstOrDefault();

            if (Selected is null)
                return Page();

            // El tutor elige días locales; 'To' es inclusivo hasta el final de ese día en México.
            var all = await _guardians.GetMovementsAsync(
                Selected.AccountId, MxTime.StartOfDayUtc(From), MxTime.EndOfDayUtc(To));

            // Los totales resumen el rango completo, no sólo la pestaña activa.
            TotalIn = all.Where(m => m.Amount > 0).Sum(m => m.Amount);
            TotalOut = -all.Where(m => m.Amount < 0).Sum(m => m.Amount);

            var filtered = Kind switch
            {
                MovementFilter.TopUps => all.Where(m => m.Type == nameof(MovementType.TopUp)).ToList(),
                MovementFilter.Purchases => all.Where(m => m.Type == nameof(MovementType.Sale)).ToList(),
                _ => all,
            };

            AllFilteredMovements = filtered;
            FilteredCount = filtered.Count;
            TotalPages = FilteredCount == 0 ? 1 : (int)Math.Ceiling(FilteredCount / (double)PageSize);
            if (PageNumber > TotalPages)
                PageNumber = TotalPages;

            Movements = filtered.Skip((PageNumber - 1) * PageSize).Take(PageSize).ToList();
        }
        catch (Exception ex)
        {
            Error = $"No se pudieron cargar tus movimientos: {ex.Message}";
        }
        return Page();
    }

    /// <summary>Etiqueta en español para el tipo de movimiento del libro mayor.</summary>
    public static string TypeLabel(string type) => type switch
    {
        nameof(MovementType.TopUp) => "Recarga",
        nameof(MovementType.Sale) => "Compra",
        nameof(MovementType.Refund) => "Devolución",
        nameof(MovementType.Adjustment) => "Ajuste",
        _ => type,
    };
}
