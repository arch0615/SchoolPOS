using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SchoolPOS.Data;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Common;
using SchoolPOS.Domain.Enums;
using SchoolPOS.Portal.Web.Infrastructure;
using SchoolPOS.Portal.Web.Infrastructure.Email;

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
    private readonly SchoolDbContext _db;
    private readonly IEmailSender _email;
    private readonly CommissionInvoicePdfRenderer _pdfRenderer;
    private readonly ILogger<CommissionsModel> _logger;

    public CommissionsModel(
        ICommissionReportService reports, ICommissionInvoiceService invoices, SchoolDbContext db,
        IEmailSender email, CommissionInvoicePdfRenderer pdfRenderer, ILogger<CommissionsModel> logger)
    {
        _reports = reports;
        _invoices = invoices;
        _db = db;
        _email = email;
        _pdfRenderer = pdfRenderer;
        _logger = logger;
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
        try
        {
            // Días locales (México) → instantes UTC, igual que el resto de las consolas.
            Rollup = await _reports.GetVendorRollupAsync(MxTime.StartOfDayUtc(From), MxTime.EndOfDayUtc(To));
        }
        catch (Exception ex)
        {
            Error = $"No se pudo cargar la comisión: {ex.Message}";
        }
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
            // Mismo criterio de día local que el reporte, para que lo facturado coincida
            // exactamente con la comisión que la pantalla muestra para ese periodo.
            var invoice = await _invoices.IssueForPeriodAsync(
                schoolId, MxTime.ToUtc(from.Value.Date), MxTime.EndOfDayUtc(to)!.Value);
            if (invoice.Status == CfdiStatus.Stamped)
            {
                Message = $"CFDI emitido: {invoice.Uuid} · {invoice.CommissionAmount:C2}";
                // Nunca debe poder tumbar la respuesta: el CFDI ya quedó timbrado y guardado.
                await CommissionInvoiceNotifier.SendStampedAsync(_db, _email, _pdfRenderer, _logger, invoice.Id);
            }
            else
            {
                Message = $"No se pudo timbrar: {invoice.Error}";
            }
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }

        return RedirectToPage(new { From = from, To = to });
    }
}
