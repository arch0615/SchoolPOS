using Microsoft.EntityFrameworkCore;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Data.Services;

/// <summary>
/// Implementación de reportes de ventas. Agrega en memoria (portable entre proveedores; SQLite no
/// traduce <c>SUM(decimal)</c>).
/// </summary>
public sealed class SalesReportService : ISalesReportService
{
    private readonly SchoolDbContext _db;

    public SalesReportService(SchoolDbContext db) => _db = db;

    public async Task<SalesSummary> GetSummaryAsync(
        Guid schoolId, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default)
    {
        var rows = await SalesInRange(schoolId, fromUtc, toUtc)
            .Select(s => new { s.Total, s.Tender })
            .ToListAsync(ct);

        return new SalesSummary(
            fromUtc, toUtc,
            rows.Count,
            Round(rows.Sum(r => r.Total)),
            Round(rows.Where(r => r.Tender == TenderType.Balance).Sum(r => r.Total)),
            Round(rows.Where(r => r.Tender == TenderType.Cash).Sum(r => r.Total)));
    }

    public async Task<IReadOnlyList<ProductSalesRow>> GetByProductAsync(
        Guid schoolId, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default)
    {
        var lines = await
            (from line in _db.SaleLines.AsNoTracking()
             join sale in SalesInRange(schoolId, fromUtc, toUtc) on line.SaleId equals sale.Id
             select new { line.ProductId, line.Description, line.Quantity, line.LineTotal })
            .ToListAsync(ct);

        return lines
            .GroupBy(l => new { l.ProductId, l.Description })
            .Select(g => new ProductSalesRow(
                g.Key.ProductId, g.Key.Description, g.Sum(x => x.Quantity), Round(g.Sum(x => x.LineTotal))))
            .OrderByDescending(r => r.Revenue)
            .ToList();
    }

    public async Task<IReadOnlyList<CashierSalesRow>> GetByCashierAsync(
        Guid schoolId, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default)
    {
        var rows = await SalesInRange(schoolId, fromUtc, toUtc)
            .Select(s => new { s.CashierId, s.Total })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.CashierId)
            .Select(g => new CashierSalesRow(g.Key, g.Count(), Round(g.Sum(x => x.Total))))
            .OrderByDescending(r => r.Total)
            .ToList();
    }

    private IQueryable<Domain.Entities.Sale> SalesInRange(Guid schoolId, DateTime? fromUtc, DateTime? toUtc)
    {
        var query = _db.Sales.AsNoTracking().Where(s => s.SchoolId == schoolId);
        if (fromUtc is { } from)
            query = query.Where(s => s.CreatedAtUtc >= from);
        if (toUtc is { } to)
            query = query.Where(s => s.CreatedAtUtc <= to);
        return query;
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
