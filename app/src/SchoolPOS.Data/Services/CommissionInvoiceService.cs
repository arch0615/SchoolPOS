using Microsoft.EntityFrameworkCore;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Entities;
using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Data.Services;

/// <summary>
/// Orquesta la facturación de la comisión: calcula la comisión capturada del periodo, valida los
/// datos fiscales de la escuela, arma la solicitud de CFDI, la timbra vía <see cref="ICfdiIssuer"/>
/// y persiste el registro <see cref="CommissionInvoice"/> (con UUID o error).
/// </summary>
public sealed class CommissionInvoiceService : ICommissionInvoiceService
{
    private readonly SchoolDbContext _db;
    private readonly ICommissionReportService _reports;
    private readonly ICfdiIssuer _issuer;
    private readonly CfdiSettings _settings;
    private readonly IClock _clock;

    public CommissionInvoiceService(
        SchoolDbContext db, ICommissionReportService reports, ICfdiIssuer issuer,
        CfdiSettings settings, IClock clock)
    {
        _db = db;
        _reports = reports;
        _issuer = issuer;
        _settings = settings;
        _clock = clock;
    }

    public async Task<CommissionInvoice> IssueForPeriodAsync(
        Guid schoolId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        var summary = await _reports.GetSchoolSummaryAsync(schoolId, fromUtc, toUtc, ct);
        if (summary.TotalCommission <= 0m)
            throw new InvalidOperationException("No hay comisión que facturar en el periodo indicado.");

        var school = await _db.Schools.FirstOrDefaultAsync(s => s.Id == schoolId, ct)
            ?? throw new InvalidOperationException($"Escuela {schoolId} no encontrada.");

        var receiver = BuildReceiver(school); // valida datos fiscales

        var concept = $"{_settings.ConceptTemplate} ({fromUtc:yyyy-MM-dd} a {toUtc:yyyy-MM-dd})";
        var request = new CommissionInvoiceRequest(
            receiver, summary.TotalCommission, _settings.TaxRate, _settings.TaxInclusive,
            school.Currency, concept, fromUtc, toUtc);

        var now = _clock.UtcNow;
        var invoice = new CommissionInvoice
        {
            SchoolId = schoolId,
            PeriodFromUtc = fromUtc,
            PeriodToUtc = toUtc,
            CommissionAmount = summary.TotalCommission,
            Currency = school.Currency,
            CreatedAtUtc = now,
        };

        var result = await _issuer.IssueAsync(request, ct);
        if (result.Success)
        {
            invoice.Status = CfdiStatus.Stamped;
            invoice.Uuid = result.Uuid;
            invoice.StampedXml = result.StampedXml;
            invoice.StampedAtUtc = now;
        }
        else
        {
            invoice.Status = CfdiStatus.Failed;
            invoice.Error = result.Error;
        }

        _db.CommissionInvoices.Add(invoice);
        await _db.SaveChangesAsync(ct);
        return invoice;
    }

    private static CfdiReceiver BuildReceiver(School school)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(school.Rfc)) missing.Add("RFC");
        if (string.IsNullOrWhiteSpace(school.TaxRegime)) missing.Add("régimen fiscal");
        if (string.IsNullOrWhiteSpace(school.PostalCode)) missing.Add("código postal");
        if (string.IsNullOrWhiteSpace(school.CfdiUse)) missing.Add("uso de CFDI");
        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"Faltan datos fiscales de la escuela para facturar: {string.Join(", ", missing)}.");

        var name = string.IsNullOrWhiteSpace(school.LegalName) ? school.Name : school.LegalName!;
        return new CfdiReceiver(school.Rfc!, name, school.TaxRegime!, school.PostalCode!, school.CfdiUse!);
    }
}
