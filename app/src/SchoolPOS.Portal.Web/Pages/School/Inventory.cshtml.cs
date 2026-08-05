using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SchoolPOS.Data;
using SchoolPOS.Portal.Web.Infrastructure;

namespace SchoolPOS.Portal.Web.Pages.School;

/// <summary>Inventario de la propia escuela (solo lectura). Limitado por el claim school_id.</summary>
[Authorize(Policy = "SchoolInventory")]
public class InventoryModel : PageModel
{
    private readonly SchoolDbContext _db;
    public InventoryModel(SchoolDbContext db) => _db = db;

    public int ActiveCount { get; private set; }
    public int LowCount { get; private set; }
    public decimal InventoryValue { get; private set; }
    public int CategoryCount { get; private set; }
    public IReadOnlyList<Row> Rows { get; private set; } = Array.Empty<Row>();

    public async Task OnGetAsync()
    {
        var schoolId = User.GetSchoolId();
        var products = await _db.Products.Where(p => p.SchoolId == schoolId).ToListAsync();
        var catName = (await _db.Categories.Where(c => c.SchoolId == schoolId).ToListAsync())
            .ToDictionary(c => c.Id, c => c.Name);

        ActiveCount = products.Count(p => p.IsActive);
        LowCount = products.Count(p => p.IsActive && p.StockOnHand <= p.MinStock);
        InventoryValue = products.Where(p => p.IsActive).Sum(p => p.StockOnHand * p.Cost);
        CategoryCount = catName.Count;

        string CatOf(Guid? id) => id.HasValue && catName.TryGetValue(id.Value, out var n) ? n : "Sin categoría";

        Rows = products
            .OrderBy(p => CatOf(p.CategoryId))
            .ThenBy(p => p.Name)
            .Select(p => new Row(p.Name, CatOf(p.CategoryId), p.Price, p.StockOnHand, p.MinStock,
                p.IsActive, p.IsActive && p.StockOnHand <= p.MinStock))
            .ToList();
    }

    public sealed record Row(string Name, string Category, decimal Price, decimal Stock, decimal Min, bool Active, bool Low);
}
