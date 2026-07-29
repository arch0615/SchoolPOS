using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Portal.Web.Pages.Vendor;

/// <summary>
/// Detalle de comisión por escuela en el periodo (FR-COM-3/FR-COM-4) y emisión del CFDI de
/// comisión (FR-COM-5), que siempre se factura por el periodo seleccionado.
/// </summary>
[Authorize(Policy = "Vendor")]
public class CommissionsModel : PageModel
{
    private readonly ICommissionReportService _reports;
    private readonly ICommissionInvoiceService _invoices;

    public CommissionsModel(ICommissionReportService reports, ICommissionInvoiceService invoices)
    {
        _reports = reports;
        _invoices = invoices;
    }

    public VendorCommissionRollup Rollup { get; private set; } =
        new(0m, 0m, 0, Array.Empty<SchoolCommissionSummary>());

    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }

    [TempData] public string? Message { get; set; }
    [TempData] public string? Error { get; set; }

    public decimal AverageRate => Rollup.TotalRecharged == 0m
        ? 0m
        : Math.Round(Rollup.TotalCommission / Rollup.TotalRecharged, 4);

    public async Task OnGetAsync()
    {
        var toUtc = To?.Date.AddDays(1).AddTicks(-1);
        Rollup = await _reports.GetVendorRollupAsync(From?.Date, toUtc);
    }

    public async Task<IActionResult> OnPostIssueAsync(Guid schoolId, DateTime? from, DateTime? to)
    {
        if (from is null || to is null)
        {
            Error = "Selecciona un periodo (Desde y Hasta) para emitir la factura.";
            return RedirectToPage(new { From = from, To = to });
        }

        try
        {
            var toUtc = to.Value.Date.AddDays(1).AddTicks(-1);
            var invoice = await _invoices.IssueForPeriodAsync(schoolId, from.Value.Date, toUtc);
            Message = invoice.Status == CfdiStatus.Stamped
                ? $"CFDI emitido: {invoice.Uuid} · {invoice.CommissionAmount:C2}"
                : $"No se pudo timbrar: {invoice.Error}";
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }

        return RedirectToPage(new { From = from, To = to });
    }
}
