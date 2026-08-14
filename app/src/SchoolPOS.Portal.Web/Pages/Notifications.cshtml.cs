using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Portal.Web.Infrastructure;

namespace SchoolPOS.Portal.Web.Pages;

/// <summary>
/// Avisos (FR-WP-10): el tutor elige qué quiere que se le notifique. Sólo guarda la elección;
/// el envío de correos se implementa aparte y consultará estas banderas.
/// </summary>
[Authorize(Policy = "Guardian")]
public class NotificationsModel : PageModel
{
    private readonly INotificationPreferenceService _prefs;
    private readonly IGuardianService _guardians;
    private readonly PortalOptions _options;

    public NotificationsModel(
        INotificationPreferenceService prefs, IGuardianService guardians, PortalOptions options)
    {
        _prefs = prefs;
        _guardians = guardians;
        _options = options;
    }

    [BindProperty] public bool LowBalance { get; set; }
    [BindProperty] public bool TopUpConfirmed { get; set; }
    [BindProperty] public bool PurchaseMade { get; set; }
    [BindProperty] public bool DailySummary { get; set; }

    public string? Email { get; private set; }
    public decimal LowBalanceThreshold => _options.LowBalanceThreshold;

    [TempData] public string? Message { get; set; }
    [TempData] public string? Error { get; set; }

    public async Task OnGetAsync()
    {
        try
        {
            var guardianId = User.GetGuardianId();
            var prefs = await _prefs.GetAsync(guardianId);

            LowBalance = prefs.LowBalance;
            TopUpConfirmed = prefs.TopUpConfirmed;
            PurchaseMade = prefs.PurchaseMade;
            DailySummary = prefs.DailySummary;

            Email = (await _guardians.GetAsync(guardianId))?.Email;
        }
        catch (Exception ex)
        {
            Error = $"No se pudieron cargar tus preferencias: {ex.Message}";
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        try
        {
            await _prefs.SaveAsync(
                User.GetGuardianId(), LowBalance, TopUpConfirmed, PurchaseMade, DailySummary);
            Message = "Preferencias de aviso guardadas.";
        }
        catch (Exception ex)
        {
            Error = $"No se pudieron guardar tus preferencias: {ex.Message}";
        }
        return RedirectToPage();
    }
}
