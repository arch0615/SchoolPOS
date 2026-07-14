using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SchoolPOS.Domain.Abstractions;

namespace SchoolPOS.Portal.Web.Pages.Vendor;

[Authorize(Policy = "Vendor")]
public class IndexModel : PageModel
{
    private readonly ICommissionReportService _reports;

    public IndexModel(ICommissionReportService reports) => _reports = reports;

    public VendorCommissionRollup Rollup { get; private set; } =
        new(0m, 0m, 0, Array.Empty<SchoolCommissionSummary>());

    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }

    public async Task OnGetAsync()
    {
        var toUtc = To?.Date.AddDays(1).AddTicks(-1); // fin del día inclusivo
        Rollup = await _reports.GetVendorRollupAsync(From?.Date, toUtc);
    }

    public decimal AverageRate => Rollup.TotalRecharged == 0m
        ? 0m
        : Math.Round(Rollup.TotalCommission / Rollup.TotalRecharged, 4);
}
