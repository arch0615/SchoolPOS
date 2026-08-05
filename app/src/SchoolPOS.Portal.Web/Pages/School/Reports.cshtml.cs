using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SchoolPOS.Data;
using SchoolPOS.Domain.Enums;
using SchoolPOS.Portal.Web.Infrastructure;

namespace SchoolPOS.Portal.Web.Pages.School;

/// <summary>Reportes de ventas de la propia escuela (solo lectura). Limitado por el claim school_id.</summary>
[Authorize(Policy = "SchoolAdmin")]
public class ReportsModel : PageModel
{
    private readonly SchoolDbContext _db;
    public ReportsModel(SchoolDbContext db) => _db = db;

    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }

    public DateTime FromDate { get; private set; }
    public DateTime ToDate { get; private set; }
    public decimal Total { get; private set; }
    public int Tickets { get; private set; }
    public decimal Avg => Tickets == 0 ? 0m : Total / Tickets;
    public decimal CashTotal { get; private set; }
    public decimal BalanceTotal { get; private set; }

    public IReadOnlyList<DayRow> ByDay { get; private set; } = Array.Empty<DayRow>();
    public IReadOnlyList<TopRow> TopProducts { get; private set; } = Array.Empty<TopRow>();
    public IReadOnlyList<CatRow> ByCategory { get; private set; } = Array.Empty<CatRow>();

    public async Task OnGetAsync()
    {
        var schoolId = User.GetSchoolId();
        var nowLocal = MxTime.Local(DateTime.UtcNow).Date;
        FromDate = From?.Date ?? nowLocal.AddDays(-29);
        ToDate = To?.Date ?? nowLocal;
        var fromUtc = MxTime.ToUtc(FromDate);
        var toUtc = MxTime.ToUtc(ToDate.AddDays(1));

        var sales = await _db.Sales
            .Where(s => s.SchoolId == schoolId && s.CreatedAtUtc >= fromUtc && s.CreatedAtUtc < toUtc)
            .ToListAsync();
        var ids = sales.Select(s => s.Id).ToHashSet();
        var lines = await _db.SaleLines.Where(l => ids.Contains(l.SaleId)).ToListAsync();
        var products = await _db.Products.Where(p => p.SchoolId == schoolId).ToListAsync();
        var catName = (await _db.Categories.Where(c => c.SchoolId == schoolId).ToListAsync())
            .ToDictionary(c => c.Id, c => c.Name);

        var refunded = sales.Where(s => s.Status == SaleStatus.Refunded).Select(s => s.Id).ToHashSet();
        var valid = sales.Where(s => !refunded.Contains(s.Id)).ToList();
        var validLines = lines.Where(l => !refunded.Contains(l.SaleId)).ToList();

        Total = valid.Sum(s => s.Total);
        Tickets = valid.Count;
        CashTotal = valid.Where(s => s.Tender == TenderType.Cash).Sum(s => s.Total);
        BalanceTotal = valid.Where(s => s.Tender == TenderType.Balance).Sum(s => s.Total);

        // Ventas por día local.
        var perDay = valid.GroupBy(s => MxTime.Local(s.CreatedAtUtc).Date)
            .ToDictionary(g => g.Key, g => (g.Sum(x => x.Total), g.Count()));
        var days = new List<DayRow>();
        for (var d = FromDate; d <= ToDate; d = d.AddDays(1))
        {
            perDay.TryGetValue(d, out var v);
            days.Add(new DayRow(d, v.Item1, v.Item2));
        }
        ByDay = days;

        var prodCat = products.ToDictionary(p => p.Id, p => p.CategoryId);
        TopProducts = validLines.GroupBy(l => l.Description)
            .Select(g => new TopRow(g.Key, g.Sum(x => x.Quantity), g.Sum(x => x.LineTotal)))
            .OrderByDescending(t => t.Amount).Take(10).ToList();

        var catAgg = new Dictionary<string, decimal>();
        foreach (var l in validLines)
        {
            var name = "Sin categoría";
            if (prodCat.TryGetValue(l.ProductId, out var cid) && cid is Guid g && catName.TryGetValue(g, out var n))
                name = n;
            catAgg.TryGetValue(name, out var cur);
            catAgg[name] = cur + l.LineTotal;
        }
        var catTotal = catAgg.Values.Sum();
        ByCategory = catAgg.OrderByDescending(kv => kv.Value)
            .Select(kv => new CatRow(kv.Key, kv.Value, catTotal == 0 ? 0 : (double)(kv.Value / catTotal) * 100))
            .ToList();
    }

    public sealed record DayRow(DateTime Day, decimal Total, int Tickets);
    public sealed record TopRow(string Name, decimal Qty, decimal Amount);
    public sealed record CatRow(string Name, decimal Amount, double Pct);
}
