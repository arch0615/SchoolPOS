using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Enums;
using SchoolPOS.Portal.Web.Infrastructure;

namespace SchoolPOS.Portal.Web.Pages;

/// <summary>
/// Monedero (FR-WP-6): recarga dedicada, fuera del panel. Muestra el saldo del alumno
/// seleccionado, los montos sugeridos y las últimas recargas antes de ir a la pasarela.
/// </summary>
[Authorize(Policy = "Guardian")]
public class WalletModel : PageModel
{
    private const int RecentTopUps = 5;

    private readonly IGuardianService _guardians;
    private readonly ITopUpService _topUps;
    private readonly PortalOptions _options;

    public WalletModel(IGuardianService guardians, ITopUpService topUps, PortalOptions options)
    {
        _guardians = guardians;
        _topUps = topUps;
        _options = options;
    }

    public IReadOnlyList<LinkedStudent> Students { get; private set; } = Array.Empty<LinkedStudent>();
    public LinkedStudent? Selected { get; private set; }
    public IReadOnlyList<MovementRow> Recent { get; private set; } = Array.Empty<MovementRow>();

    public decimal MinTopUp => _options.MinTopUp;
    public decimal MaxTopUp => _options.MaxTopUp;
    public decimal[] Presets => _options.TopUpPresets;
    public decimal LowBalanceThreshold => _options.LowBalanceThreshold;

    [TempData] public string? Message { get; set; }
    [TempData] public string? Error { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid? studentId)
    {
        await LoadAsync(studentId);
        return Page();
    }

    public async Task<IActionResult> OnPostTopUpAsync(Guid studentId, Guid accountId, decimal amount)
    {
        // Control de acceso primero: no revelamos nada sobre cuentas ajenas al validar el monto.
        if (!await _guardians.OwnsStudentAsync(User.GetGuardianId(), studentId))
        {
            Error = "No tiene acceso a esa cuenta.";
            return RedirectToPage();
        }

        if (amount < _options.MinTopUp || amount > _options.MaxTopUp)
        {
            Error = $"El monto debe estar entre {_options.MinTopUp:C2} y {_options.MaxTopUp:C2}.";
            return RedirectToPage(new { studentId });
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

        if (Selected is null)
            return;

        var movements = await _guardians.GetMovementsAsync(Selected.AccountId, null, null);
        Recent = movements
            .Where(m => m.Type == nameof(MovementType.TopUp))
            .Take(RecentTopUps)
            .ToList();
    }
}
