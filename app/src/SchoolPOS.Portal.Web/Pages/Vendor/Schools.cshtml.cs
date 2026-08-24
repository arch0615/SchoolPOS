using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SchoolPOS.Data;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Common;

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

    [TempData] public string? Message { get; set; }
    [TempData] public string? RateError { get; set; }

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

    /// <summary>
    /// Cambia la tarifa de comisión de una escuela. Vive aquí y no en la configuración del POS
    /// porque es una condición del contrato entre proveedor y escuela: la escuela la consulta, no
    /// la negocia sola. Es además la tarifa que el portal aplica al crear cada recarga.
    /// </summary>
    public async Task<IActionResult> OnPostRateAsync(Guid schoolId, decimal commissionRate)
    {
        if (commissionRate < 0m || commissionRate > 1m)
        {
            RateError = "La comisión debe estar entre 0 y 1 (por ejemplo 0.05 para 5%).";
            return RedirectToPage(new { From, To });
        }

        try
        {
            var school = await _db.Schools.FirstOrDefaultAsync(s => s.Id == schoolId);
            if (school is null)
            {
                RateError = "No se encontró la escuela.";
                return RedirectToPage(new { From, To });
            }

            // Las recargas ya creadas conservan la tarifa con la que se cobraron (TopUp guarda su
            // propio CommissionRate), así que este cambio solo aplica de aquí en adelante.
            school.CommissionRate = commissionRate;
            await _db.SaveChangesAsync();
            Message = $"Comisión de {school.Name} actualizada a {(commissionRate * 100m).ToString("0.##")}%.";
        }
        catch (Exception ex)
        {
            RateError = $"No se pudo actualizar la comisión: {ex.Message}";
        }

        return RedirectToPage(new { From, To });
    }

    private async Task LoadAsync()
    {
        // Las fechas del filtro son días locales; la consulta va en UTC.
        var rollup = await _reports.GetVendorRollupAsync(MxTime.StartOfDayUtc(From), MxTime.EndOfDayUtc(To));
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
                s.CfdiUse,
                Connected = a != null,
                // Nullable explícito: con LEFT JOIN sin conexión la columna llega NULL.
                ConnectedAtUtc = a != null ? (DateTime?)a.ConnectedAtUtc : null,
            })
            .ToListAsync();

        Rows = schools.Select(s =>
        {
            activity.TryGetValue(s.Id, out var act);
            // Debe calzar exactamente con lo que CommissionInvoiceService.BuildReceiver exige,
            // o el indicador "listo para facturar" mentiría (CfdiUse faltaba aquí antes).
            var fiscalComplete =
                !string.IsNullOrWhiteSpace(s.Rfc) &&
                !string.IsNullOrWhiteSpace(s.LegalName) &&
                !string.IsNullOrWhiteSpace(s.TaxRegime) &&
                !string.IsNullOrWhiteSpace(s.PostalCode) &&
                !string.IsNullOrWhiteSpace(s.CfdiUse);

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
