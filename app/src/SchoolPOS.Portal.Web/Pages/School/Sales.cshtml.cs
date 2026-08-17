using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SchoolPOS.Data;
using SchoolPOS.Domain.Common;
using SchoolPOS.Domain.Enums;
using SchoolPOS.Portal.Web.Infrastructure;

namespace SchoolPOS.Portal.Web.Pages.School;

/// <summary>Ventas de la propia escuela (solo lectura). Limitado por el claim school_id.</summary>
[Authorize(Policy = "School")]
public class SalesModel : PageModel
{
    private readonly SchoolDbContext _db;
    public SalesModel(SchoolDbContext db) => _db = db;

    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }

    public decimal Total { get; private set; }
    public int Count { get; private set; }
    public decimal Avg => Count == 0 ? 0m : Total / Count;
    public decimal CashTotal { get; private set; }
    public decimal BalanceTotal { get; private set; }
    public IReadOnlyList<Row> Rows { get; private set; } = Array.Empty<Row>();

    public async Task OnGetAsync()
    {
        var schoolId = User.GetSchoolId();
        var fromUtc = From.HasValue ? MxTime.ToUtc(From.Value.Date) : DateTime.UtcNow.AddDays(-30);
        var toUtc = To.HasValue ? MxTime.ToUtc(To.Value.Date.AddDays(1)) : DateTime.UtcNow.AddDays(1);

        var sales = await _db.Sales
            .Where(s => s.SchoolId == schoolId && s.CreatedAtUtc >= fromUtc && s.CreatedAtUtc < toUtc)
            .OrderByDescending(s => s.CreatedAtUtc)
            .Take(300)
            .ToListAsync();

        var ids = sales.Select(s => s.Id).ToHashSet();
        var items = (await _db.SaleLines.Where(l => ids.Contains(l.SaleId)).Select(l => l.SaleId).ToListAsync())
            .GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());

        var valid = sales.Where(s => s.Status != SaleStatus.Refunded).ToList();
        Total = valid.Sum(s => s.Total);
        Count = valid.Count;
        CashTotal = valid.Where(s => s.Tender == TenderType.Cash).Sum(s => s.Total);
        BalanceTotal = valid.Where(s => s.Tender == TenderType.Balance).Sum(s => s.Total);

        Rows = sales.Select(s => new Row(
            s.CreatedAtUtc, s.Tender, s.Status, items.GetValueOrDefault(s.Id, 0), s.Total)).ToList();
    }

    public sealed record Row(DateTime CreatedAtUtc, TenderType Tender, SaleStatus Status, int Items, decimal Total);
}
