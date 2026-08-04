using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SchoolPOS.Data;
using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Portal.Web.Infrastructure;

public sealed record StoreTopProduct(string Name, decimal Qty, decimal Amount);
public sealed record StoreLowStock(string Name, decimal Stock, decimal Min);
public sealed record StoreRecentSale(DateTime CreatedAtUtc, decimal Total, TenderType Tender, SaleStatus Status);
public sealed record StoreDayBar(string Label, decimal Total, bool IsToday);
public sealed record StoreCategorySlice(string Name, decimal Amount, double Pct);

public sealed record StoreDashboardData(
    decimal SalesToday, int TicketsToday, decimal AvgTicket,
    bool CashSessionOpen, decimal CashInDrawer, decimal OpeningFloat, int CashSalesToday,
    int ActiveProducts, int LowStockCount, decimal InventoryValue,
    IReadOnlyList<StoreTopProduct> TopProducts,
    IReadOnlyList<StoreLowStock> LowStock,
    IReadOnlyList<StoreRecentSale> RecentSales,
    IReadOnlyList<StoreDayBar> Trend7d,
    IReadOnlyList<StoreCategorySlice> Categories)
{
    public decimal CashSalesAmount => CashSessionOpen ? CashInDrawer - OpeningFloat : 0m;
}

/// <summary>
/// Arma el resumen de la tienda (ventas, caja, inventario) para el panel web. SQLite guarda los
/// decimales como TEXTO, así que las sumas/comparaciones se hacen en memoria (el volumen por
/// escuela es pequeño). Con <paramref name="schoolId"/> nulo agrega todas las escuelas (vista del
/// proveedor); con un id, se limita a esa escuela (vista de la propia tienda).
/// </summary>
public static class StoreDashboard
{
    private static readonly TimeZoneInfo Tz = ResolveTz();
    private static readonly CultureInfo EsMx = CultureInfo.GetCultureInfo("es-MX");

    public static async Task<StoreDashboardData> BuildAsync(SchoolDbContext db, Guid? schoolId, CancellationToken ct = default)
    {
        var nowUtc = DateTime.UtcNow;
        var todayLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, Tz).Date;
        var startUtc = ToUtc(todayLocal);
        var since = nowUtc.AddDays(-30);

        var recent = await db.Sales
            .Where(s => (schoolId == null || s.SchoolId == schoolId) && s.CreatedAtUtc >= since)
            .ToListAsync(ct);
        var ids = recent.Select(s => s.Id).ToHashSet();
        var lines = await db.SaleLines.Where(l => ids.Contains(l.SaleId)).ToListAsync(ct);
        var products = await db.Products
            .Where(p => (schoolId == null || p.SchoolId == schoolId) && p.IsActive)
            .ToListAsync(ct);
        var cats = await db.Categories
            .Where(c => schoolId == null || c.SchoolId == schoolId)
            .ToListAsync(ct);
        var session = await db.CashSessions
            .Where(c => (schoolId == null || c.SchoolId == schoolId) && c.Status == CashSessionStatus.Open)
            .OrderByDescending(c => c.OpenedAtUtc)
            .FirstOrDefaultAsync(ct);

        var refunded = recent.Where(s => s.Status == SaleStatus.Refunded).Select(s => s.Id).ToHashSet();
        var valid = recent.Where(s => !refunded.Contains(s.Id)).ToList();
        var validLines = lines.Where(l => !refunded.Contains(l.SaleId)).ToList();
        var today = valid.Where(s => s.CreatedAtUtc >= startUtc).ToList();

        var salesToday = today.Sum(s => s.Total);
        var tickets = today.Count;
        var avg = tickets == 0 ? 0m : salesToday / tickets;

        var cashTodayAmt = today.Where(s => s.Tender == TenderType.Cash).Sum(s => s.Total);
        var opening = session?.OpeningFloat ?? 0m;
        var drawer = session is null ? 0m : opening + cashTodayAmt;

        var active = products.Count;
        var low = products.Where(p => p.StockOnHand <= p.MinStock).OrderBy(p => p.StockOnHand).ToList();
        var invValue = products.Sum(p => p.StockOnHand * p.Cost);

        var top = validLines
            .GroupBy(l => l.Description)
            .Select(g => new StoreTopProduct(g.Key, g.Sum(x => x.Quantity), g.Sum(x => x.LineTotal)))
            .OrderByDescending(t => t.Qty).Take(6).ToList();

        // Ventas por categoría (mapea renglón → producto → categoría).
        var prodCat = products.ToDictionary(p => p.Id, p => p.CategoryId);
        var catName = cats.ToDictionary(c => c.Id, c => c.Name);
        var catAgg = new Dictionary<string, decimal>();
        decimal catTotal = 0m;
        foreach (var l in validLines)
        {
            var name = "Sin categoría";
            if (prodCat.TryGetValue(l.ProductId, out var cid) && cid is Guid g && catName.TryGetValue(g, out var n))
                name = n;
            catAgg.TryGetValue(name, out var cur);
            catAgg[name] = cur + l.LineTotal;
            catTotal += l.LineTotal;
        }
        var categories = catAgg.OrderByDescending(kv => kv.Value)
            .Select(kv => new StoreCategorySlice(kv.Key, kv.Value, catTotal == 0 ? 0 : (double)(kv.Value / catTotal) * 100))
            .ToList();

        // Tendencia de 7 días (por día local).
        var trend = new List<StoreDayBar>();
        for (var i = 6; i >= 0; i--)
        {
            var day = todayLocal.AddDays(-i);
            var ds = ToUtc(day);
            var de = ToUtc(day.AddDays(1));
            var tot = valid.Where(s => s.CreatedAtUtc >= ds && s.CreatedAtUtc < de).Sum(s => s.Total);
            trend.Add(new StoreDayBar(Cap(day.ToString("ddd", EsMx)), tot, i == 0));
        }

        var low8 = low.Take(8).Select(p => new StoreLowStock(p.Name, p.StockOnHand, p.MinStock)).ToList();
        var recentSales = recent.OrderByDescending(s => s.CreatedAtUtc).Take(10)
            .Select(s => new StoreRecentSale(s.CreatedAtUtc, s.Total, s.Tender, s.Status)).ToList();

        return new StoreDashboardData(salesToday, tickets, avg, session is not null, drawer, opening,
            today.Count(s => s.Tender == TenderType.Cash), active, low.Count, invValue,
            top, low8, recentSales, trend, categories);
    }

    private static DateTime ToUtc(DateTime localDate) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localDate, DateTimeKind.Unspecified), Tz);

    private static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0], EsMx) + s[1..];

    private static TimeZoneInfo ResolveTz()
    {
        foreach (var id in new[] { "America/Mexico_City", "Central Standard Time (Mexico)" })
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { }
        return TimeZoneInfo.Utc;
    }
}
