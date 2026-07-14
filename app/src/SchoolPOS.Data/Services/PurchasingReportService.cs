using Microsoft.EntityFrameworkCore;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Data.Services;

/// <summary>Reportes de compras. Agrega en memoria (portable entre proveedores). Excluye canceladas.</summary>
public sealed class PurchasingReportService : IPurchasingReportService
{
    private readonly SchoolDbContext _db;

    public PurchasingReportService(SchoolDbContext db) => _db = db;

    public async Task<PurchasingSummary> GetSummaryAsync(
        Guid schoolId, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default)
    {
        var totals = await OrdersInRange(schoolId, fromUtc, toUtc).Select(o => o.Total).ToListAsync(ct);
        return new PurchasingSummary(totals.Count, Round(totals.Sum()));
    }

    public async Task<IReadOnlyList<SupplierPurchaseRow>> GetBySupplierAsync(
        Guid schoolId, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default)
    {
        var rows = await
            (from o in OrdersInRange(schoolId, fromUtc, toUtc)
             join s in _db.Suppliers.AsNoTracking() on o.SupplierId equals s.Id
             select new { o.SupplierId, SupplierName = s.Name, o.Total })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => new { r.SupplierId, r.SupplierName })
            .Select(g => new SupplierPurchaseRow(g.Key.SupplierId, g.Key.SupplierName, g.Count(), Round(g.Sum(x => x.Total))))
            .OrderByDescending(r => r.Total)
            .ToList();
    }

    public async Task<IReadOnlyList<ProductPurchaseRow>> GetByProductAsync(
        Guid schoolId, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default)
    {
        var lines = await
            (from line in _db.PurchaseOrderLines.AsNoTracking()
             join o in OrdersInRange(schoolId, fromUtc, toUtc) on line.PurchaseOrderId equals o.Id
             select new { line.ProductId, line.Quantity, line.LineTotal })
            .ToListAsync(ct);

        return lines
            .GroupBy(l => l.ProductId)
            .Select(g => new ProductPurchaseRow(g.Key, g.Sum(x => x.Quantity), Round(g.Sum(x => x.LineTotal))))
            .OrderByDescending(r => r.Total)
            .ToList();
    }

    private IQueryable<Domain.Entities.PurchaseOrder> OrdersInRange(Guid schoolId, DateTime? fromUtc, DateTime? toUtc)
    {
        var query = _db.PurchaseOrders.AsNoTracking()
            .Where(o => o.SchoolId == schoolId && o.Status != PurchaseOrderStatus.Cancelled);
        if (fromUtc is { } from) query = query.Where(o => o.OrderDate >= from);
        if (toUtc is { } to) query = query.Where(o => o.OrderDate <= to);
        return query;
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
