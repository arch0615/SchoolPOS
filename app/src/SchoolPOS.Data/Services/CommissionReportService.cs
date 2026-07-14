using Microsoft.EntityFrameworkCore;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Data.Services;

/// <summary>
/// Reportes de comisión. Agrega en memoria las recargas capturadas (portable entre proveedores;
/// SQLite no traduce <c>SUM(decimal)</c>; en SQL Server la agregación también sería válida en el
/// servidor). Solo cuenta recargas confirmadas o aplicadas.
/// </summary>
public sealed class CommissionReportService : ICommissionReportService
{
    private readonly SchoolDbContext _db;

    public CommissionReportService(SchoolDbContext db) => _db = db;

    private static readonly TopUpStatus[] Captured = { TopUpStatus.Confirmed, TopUpStatus.Applied };

    public async Task<VendorCommissionRollup> GetVendorRollupAsync(
        DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default)
    {
        var rows = await CapturedTopUps(fromUtc, toUtc)
            .Select(t => new { t.SchoolId, t.Amount, t.CommissionAmount })
            .ToListAsync(ct);

        var names = await _db.Schools.AsNoTracking()
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        var perSchool = rows
            .GroupBy(r => r.SchoolId)
            .Select(g => new SchoolCommissionSummary(
                g.Key,
                names.TryGetValue(g.Key, out var name) ? name : g.Key.ToString(),
                g.Count(),
                Round(g.Sum(x => x.Amount)),
                Round(g.Sum(x => x.CommissionAmount))))
            .OrderByDescending(s => s.TotalCommission)
            .ToList();

        return new VendorCommissionRollup(
            Round(rows.Sum(r => r.Amount)),
            Round(rows.Sum(r => r.CommissionAmount)),
            rows.Count,
            perSchool);
    }

    public async Task<SchoolCommissionSummary> GetSchoolSummaryAsync(
        Guid schoolId, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default)
    {
        var rows = await CapturedTopUps(fromUtc, toUtc)
            .Where(t => t.SchoolId == schoolId)
            .Select(t => new { t.Amount, t.CommissionAmount })
            .ToListAsync(ct);

        var name = await _db.Schools.AsNoTracking()
            .Where(s => s.Id == schoolId).Select(s => s.Name).FirstOrDefaultAsync(ct)
            ?? schoolId.ToString();

        return new SchoolCommissionSummary(
            schoolId, name, rows.Count,
            Round(rows.Sum(r => r.Amount)),
            Round(rows.Sum(r => r.CommissionAmount)));
    }

    private IQueryable<Domain.Entities.TopUp> CapturedTopUps(DateTime? fromUtc, DateTime? toUtc)
    {
        var query = _db.TopUps.AsNoTracking().Where(t => Captured.Contains(t.Status));
        if (fromUtc is { } from)
            query = query.Where(t => t.CreatedAtUtc >= from);
        if (toUtc is { } to)
            query = query.Where(t => t.CreatedAtUtc <= to);
        return query;
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
