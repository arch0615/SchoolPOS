using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SchoolPOS.Data;
using SchoolPOS.Domain.Abstractions;

namespace SchoolPOS.Portal.Web.Pages.Vendor;

/// <summary>
/// Escuelas del proveedor: el padrón completo, no sólo las que tuvieron recargas en el periodo.
/// Cruza la actividad de comisión con el estado de la conexión de cobro y con la completitud de
/// los datos fiscales, que es lo que decide si se le puede emitir CFDI (FR-COM-5).
/// </summary>
[Authorize(Policy = "Vendor")]
public class SchoolsModel : PageModel
{
    private readonly ICommissionReportService _reports;
    private readonly SchoolDbContext _db;

    public SchoolsModel(ICommissionReportService reports, SchoolDbContext db)
    {
        _reports = reports;
        _db = db;
    }

    public IReadOnlyList<Row> Rows { get; private set; } = Array.Empty<Row>();

    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }

    public int ConnectedCount => Rows.Count(r => r.Connected);
    public int InvoiceReadyCount => Rows.Count(r => r.FiscalComplete);

    public string? Error { get; private set; }

    public async Task OnGetAsync()
    {
        try
        {
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Error = $"No se pudo cargar el listado de escuelas: {ex.Message}";
        }
    }

    private async Task LoadAsync()
    {
        var toUtc = To?.Date.AddDays(1).AddTicks(-1);
        var rollup = await _reports.GetVendorRollupAsync(From?.Date, toUtc);
        var activity = rollup.Schools.ToDictionary(s => s.SchoolId);

        var schools = await (
            from s in _db.Schools.AsNoTracking()
            join a in _db.SchoolPaymentAccounts.AsNoTracking()
                on new { SchoolId = s.Id, Provider = "MercadoPago" } equals new { a.SchoolId, a.Provider } into acc
            from a in acc.DefaultIfEmpty()
            orderby s.Name
            select new
            {
                s.Id,
                s.Name,
                s.CommissionRate,
                s.Currency,
                s.Rfc,
                s.LegalName,
                s.TaxRegime,
                s.PostalCode,
                Connected = a != null,
                // Nullable explícito: con LEFT JOIN sin conexión la columna llega NULL.
                ConnectedAtUtc = a != null ? (DateTime?)a.ConnectedAtUtc : null,
            })
            .ToListAsync();

        Rows = schools.Select(s =>
        {
            activity.TryGetValue(s.Id, out var act);
            var fiscalComplete =
                !string.IsNullOrWhiteSpace(s.Rfc) &&
                !string.IsNullOrWhiteSpace(s.LegalName) &&
                !string.IsNullOrWhiteSpace(s.TaxRegime) &&
                !string.IsNullOrWhiteSpace(s.PostalCode);

            return new Row(
                s.Id, s.Name, s.CommissionRate, s.Currency,
                s.Connected, s.ConnectedAtUtc,
                fiscalComplete,
                act?.TopUpCount ?? 0,
                act?.TotalRecharged ?? 0m,
                act?.TotalCommission ?? 0m);
        }).ToList();
    }

    public sealed record Row(
        Guid SchoolId, string Name, decimal CommissionRate, string Currency,
        bool Connected, DateTime? ConnectedAtUtc, bool FiscalComplete,
        int TopUpCount, decimal TotalRecharged, decimal TotalCommission);
}
