using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SchoolPOS.Domain.Abstractions;

namespace SchoolPOS.Portal.Web.Pages.Vendor;

/// <summary>
/// Panel del proveedor (FR-COM-3): indicadores del periodo y gráficas de tendencia.
/// El desglose por escuela vive en /Vendor/Schools y el detalle de comisión en
/// /Vendor/Commissions.
/// </summary>
[Authorize(Policy = "Vendor")]
public class DashboardModel : PageModel
{
    /// <summary>Ventana máxima de la serie diaria, para que la curva siga siendo legible.</summary>
    private const int MaxSeriesDays = 92;

    private readonly ICommissionReportService _reports;

    public DashboardModel(ICommissionReportService reports) => _reports = reports;

    public VendorCommissionRollup Rollup { get; private set; } =
        new(0m, 0m, 0, Array.Empty<SchoolCommissionSummary>());

    public IReadOnlyList<CommissionDailyPoint> Series { get; private set; } = Array.Empty<CommissionDailyPoint>();
    public DateTime SeriesFrom { get; private set; }
    public DateTime SeriesTo { get; private set; }

    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }

    public string? Error { get; private set; }

    public decimal AverageRate => Rollup.TotalRecharged == 0m
        ? 0m
        : Math.Round(Rollup.TotalCommission / Rollup.TotalRecharged, 4);

    public async Task OnGetAsync()
    {
        try
        {
            var toUtc = To?.Date.AddDays(1).AddTicks(-1); // fin del día inclusivo
            Rollup = await _reports.GetVendorRollupAsync(From?.Date, toUtc);

            // Serie diaria: usa el rango elegido o, por defecto, los últimos 30 días.
            var end = To?.Date ?? DateTime.UtcNow.Date;
            var start = From?.Date ?? end.AddDays(-29);
            if ((end - start).TotalDays > MaxSeriesDays) start = end.AddDays(-MaxSeriesDays);
            SeriesFrom = start;
            SeriesTo = end;
            Series = await _reports.GetDailySeriesAsync(start, end.AddDays(1).AddTicks(-1));
        }
        catch (Exception ex)
        {
            Error = $"No se pudo cargar el panel: {ex.Message}";
        }
    }
}
