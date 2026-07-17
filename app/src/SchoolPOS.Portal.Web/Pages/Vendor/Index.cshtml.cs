using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SchoolPOS.Data;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Portal.Web.Pages.Vendor;

[Authorize(Policy = "Vendor")]
public class IndexModel : PageModel
{
    private readonly ICommissionReportService _reports;
    private readonly ICommissionInvoiceService _invoices;
    private readonly SchoolDbContext _db;

    public IndexModel(ICommissionReportService reports, ICommissionInvoiceService invoices, SchoolDbContext db)
    {
        _reports = reports;
        _invoices = invoices;
        _db = db;
    }

    public VendorCommissionRollup Rollup { get; private set; } =
        new(0m, 0m, 0, Array.Empty<SchoolCommissionSummary>());

    public IReadOnlyList<InvoiceRow> Invoices { get; private set; } = Array.Empty<InvoiceRow>();

    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }

    [TempData] public string? Message { get; set; }
    [TempData] public string? Error { get; set; }

    public decimal AverageRate => Rollup.TotalRecharged == 0m
        ? 0m
        : Math.Round(Rollup.TotalCommission / Rollup.TotalRecharged, 4);

    public async Task OnGetAsync()
    {
        var toUtc = To?.Date.AddDays(1).AddTicks(-1); // fin del día inclusivo
        Rollup = await _reports.GetVendorRollupAsync(From?.Date, toUtc);

        Invoices = await (
            from ci in _db.CommissionInvoices.AsNoTracking()
            join s in _db.Schools.AsNoTracking() on ci.SchoolId equals s.Id
            orderby ci.CreatedAtUtc descending
            select new InvoiceRow(
                s.Name, ci.PeriodFromUtc, ci.PeriodToUtc, ci.CommissionAmount, ci.Currency,
                ci.Status.ToString(), ci.Uuid))
            .Take(50)
            .ToListAsync();
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

    public sealed record InvoiceRow(
        string SchoolName, DateTime PeriodFrom, DateTime PeriodTo, decimal Amount, string Currency,
        string Status, string? Uuid);
}
