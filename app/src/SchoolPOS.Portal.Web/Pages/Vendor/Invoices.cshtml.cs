using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SchoolPOS.Data;
using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Portal.Web.Pages.Vendor;

/// <summary>
/// Facturas de comisión emitidas (FR-COM-5), filtrables por escuela y estado de timbrado.
/// La emisión se hace desde /Vendor/Commissions, que es donde vive el periodo.
/// </summary>
[Authorize(Policy = "Vendor")]
public class InvoicesModel : PageModel
{
    private const int PageSize = 100;

    private readonly SchoolDbContext _db;

    public InvoicesModel(SchoolDbContext db) => _db = db;

    public IReadOnlyList<Row> Invoices { get; private set; } = Array.Empty<Row>();
    public IReadOnlyList<SchoolOption> Schools { get; private set; } = Array.Empty<SchoolOption>();

    /// <summary>Total timbrado (sólo CFDI vigentes) en el filtro actual.</summary>
    public decimal StampedTotal { get; private set; }
    public int StampedCount { get; private set; }
    public int FailedCount { get; private set; }

    [BindProperty(SupportsGet = true)] public Guid? SchoolId { get; set; }
    [BindProperty(SupportsGet = true)] public CfdiStatus? Status { get; set; }

    public async Task OnGetAsync()
    {
        Schools = await _db.Schools.AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => new SchoolOption(s.Id, s.Name))
            .ToListAsync();

        var query = from ci in _db.CommissionInvoices.AsNoTracking()
                    join s in _db.Schools.AsNoTracking() on ci.SchoolId equals s.Id
                    select new { ci, s.Name };

        if (SchoolId is { } sid)
            query = query.Where(x => x.ci.SchoolId == sid);
        if (Status is { } st)
            query = query.Where(x => x.ci.Status == st);

        Invoices = await query
            .OrderByDescending(x => x.ci.CreatedAtUtc)
            .Take(PageSize)
            .Select(x => new Row(
                x.ci.SchoolId, x.Name, x.ci.PeriodFromUtc, x.ci.PeriodToUtc,
                x.ci.CommissionAmount, x.ci.Currency, x.ci.Status, x.ci.Uuid,
                x.ci.CreatedAtUtc, x.ci.StampedAtUtc, x.ci.Error))
            .ToListAsync();

        StampedCount = Invoices.Count(i => i.Status == CfdiStatus.Stamped);
        StampedTotal = Invoices.Where(i => i.Status == CfdiStatus.Stamped).Sum(i => i.Amount);
        FailedCount = Invoices.Count(i => i.Status == CfdiStatus.Failed);
    }

    /// <summary>Etiqueta en español del estado del CFDI.</summary>
    public static string StatusLabel(CfdiStatus status) => status switch
    {
        CfdiStatus.Draft => "Borrador",
        CfdiStatus.Stamped => "Timbrada",
        CfdiStatus.Failed => "Fallida",
        CfdiStatus.Cancelled => "Cancelada",
        _ => status.ToString(),
    };

    public sealed record SchoolOption(Guid Id, string Name);

    public sealed record Row(
        Guid SchoolId, string SchoolName, DateTime PeriodFrom, DateTime PeriodTo,
        decimal Amount, string Currency, CfdiStatus Status, string? Uuid,
        DateTime CreatedAtUtc, DateTime? StampedAtUtc, string? Error);
}
