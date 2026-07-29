using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SchoolPOS.Domain.Abstractions;
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

[Authorize]
public class TransactionsModel : PageModel
{
    private readonly IGuardianService _guardians;

    public TransactionsModel(IGuardianService guardians) => _guardians = guardians;

    public IReadOnlyList<LinkedStudent> Students { get; private set; } = Array.Empty<LinkedStudent>();
    public LinkedStudent? Selected { get; private set; }
    public IReadOnlyList<MovementRow> Movements { get; private set; } = Array.Empty<MovementRow>();

    /// <summary>Suma de los abonos del rango mostrado.</summary>
    public decimal TotalIn { get; private set; }

    /// <summary>Suma de los cargos del rango mostrado, en positivo.</summary>
    public decimal TotalOut { get; private set; }

    [BindProperty(SupportsGet = true)] public Guid? StudentId { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }
    [BindProperty(SupportsGet = true)] public MovementFilter Kind { get; set; } = MovementFilter.All;

    public async Task<IActionResult> OnGetAsync()
    {
        var guardianId = User.GetGuardianId();
        Students = await _guardians.GetLinkedStudentsAsync(guardianId);
        Selected = StudentId is { } id
            ? Students.FirstOrDefault(s => s.StudentId == id) ?? Students.FirstOrDefault()
            : Students.FirstOrDefault();

        if (Selected is null)
            return Page();

        // 'To' inclusivo hasta el final del día.
        var toUtc = To?.Date.AddDays(1).AddTicks(-1);
        var all = await _guardians.GetMovementsAsync(Selected.AccountId, From?.Date, toUtc);

        // Los totales resumen el rango completo, no sólo la pestaña activa.
        TotalIn = all.Where(m => m.Amount > 0).Sum(m => m.Amount);
        TotalOut = -all.Where(m => m.Amount < 0).Sum(m => m.Amount);

        Movements = Kind switch
        {
            MovementFilter.TopUps => all.Where(m => m.Type == nameof(MovementType.TopUp)).ToList(),
            MovementFilter.Purchases => all.Where(m => m.Type == nameof(MovementType.Sale)).ToList(),
            _ => all,
        };
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
