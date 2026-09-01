using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SchoolPOS.Data;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Entities;
using SchoolPOS.Portal.Web.Infrastructure;

namespace SchoolPOS.Portal.Web.Pages.School;

/// <summary>
/// Inventario de la propia escuela — catálogo (alta/edición de productos y categorías) desde el
/// portal, sin depender del POS de escritorio. Los cambios de existencia (alta inicial, ajuste
/// por conteo) pasan por <see cref="IInventoryService"/>, igual que en el POS: nunca se escribe
/// <see cref="Product.StockOnHand"/> directamente, siempre queda el asiento del Kardex.
/// Limitado por el claim school_id.
/// </summary>
[Authorize(Policy = "SchoolInventory")]
public class InventoryModel : PageModel
{
    private readonly SchoolDbContext _db;
    private readonly IInventoryService _inventory;

    public InventoryModel(SchoolDbContext db, IInventoryService inventory)
    {
        _db = db;
        _inventory = inventory;
    }

    public int ActiveCount { get; private set; }
    public int LowCount { get; private set; }
    public decimal InventoryValue { get; private set; }
    public int CategoryCount { get; private set; }
    public IReadOnlyList<Row> Rows { get; private set; } = Array.Empty<Row>();
    public IReadOnlyList<Category> Categories { get; private set; } = Array.Empty<Category>();

    [BindProperty(SupportsGet = true)] public Guid? Edit { get; set; }
    [BindProperty] public ProductInput Input { get; set; } = new();
    // Nullable a propósito: es un [BindProperty] en una página con más de un formulario/handler
    // (SaveProduct, ToggleActive, AdjustStock…), y ASP.NET Core infiere [Required] implícito para
    // strings no anulables en TODO post a la página, aunque el handler invocado no use este campo.
    [BindProperty] public string? NewCategoryName { get; set; }

    [TempData] public string? Message { get; set; }
    [TempData] public string? Error { get; set; }

    public sealed class ProductInput
    {
        public Guid? Id { get; set; }

        [Required(ErrorMessage = "El nombre del producto es obligatorio.")]
        public string Name { get; set; } = string.Empty;

        public string? Barcode { get; set; }
        public Guid? CategoryId { get; set; }

        [Range(0, 999999, ErrorMessage = "El precio no puede ser negativo.")]
        public decimal Price { get; set; }

        [Range(0, 999999, ErrorMessage = "El costo no puede ser negativo.")]
        public decimal Cost { get; set; }

        [Range(0, 999999, ErrorMessage = "El mínimo no puede ser negativo.")]
        public decimal MinStock { get; set; }

        [Range(0, 999999, ErrorMessage = "La existencia inicial no puede ser negativa.")]
        public decimal InitialStock { get; set; }
    }

    public async Task OnGetAsync()
    {
        var schoolId = User.GetSchoolId();

        if (Edit is Guid editId)
        {
            var product = await _db.Products.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == editId && p.SchoolId == schoolId);
            if (product is not null)
            {
                Input = new ProductInput
                {
                    Id = product.Id,
                    Name = product.Name,
                    Barcode = product.Barcode,
                    CategoryId = product.CategoryId,
                    Price = product.Price,
                    Cost = product.Cost,
                    MinStock = product.MinStock,
                };
            }
        }

        await LoadAsync(schoolId);
    }

    public async Task<IActionResult> OnPostCreateCategoryAsync()
    {
        var schoolId = User.GetSchoolId();
        if (string.IsNullOrWhiteSpace(NewCategoryName))
        {
            Error = "El nombre de la categoría es obligatorio.";
            return RedirectToPage();
        }

        try
        {
            _db.Categories.Add(new Category
            {
                SchoolId = schoolId,
                Name = NewCategoryName.Trim(),
                CreatedAtUtc = DateTime.UtcNow,
            });
            await _db.SaveChangesAsync();
            Message = $"Categoría '{NewCategoryName.Trim()}' creada.";
        }
        catch (Exception ex)
        {
            Error = $"No se pudo crear la categoría: {ex.Message}";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSaveProductAsync()
    {
        var schoolId = User.GetSchoolId();

        if (!ModelState.IsValid)
        {
            Error = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault();
            return RedirectToPage(Input.Id is Guid id ? new { Edit = id } : null);
        }

        try
        {
            if (Input.Id is Guid existingId)
            {
                var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == existingId && p.SchoolId == schoolId)
                    ?? throw new InvalidOperationException("Producto no encontrado.");

                var oldPrice = product.Price;
                product.Name = Input.Name.Trim();
                product.Barcode = string.IsNullOrWhiteSpace(Input.Barcode) ? null : Input.Barcode.Trim();
                product.CategoryId = Input.CategoryId;
                product.Price = Input.Price;
                product.Cost = Input.Cost;
                product.MinStock = Input.MinStock;

                // Bitácora: un precio equivocado corregido en silencio no deja rastro de qué era
                // antes ni quién lo cambió (FR-ADM-4).
                if (oldPrice != product.Price)
                {
                    _db.AuditLogs.Add(new AuditLog
                    {
                        SchoolId = schoolId,
                        Actor = User.GetOperatorId().ToString(),
                        Action = "PriceChange",
                        Entity = nameof(Product),
                        EntityId = product.Id.ToString(),
                        Before = oldPrice.ToString("0.00"),
                        After = product.Price.ToString("0.00"),
                        CreatedAtUtc = DateTime.UtcNow,
                    });
                }

                await _db.SaveChangesAsync();
                Message = $"'{product.Name}' actualizado.";
            }
            else
            {
                var product = new Product
                {
                    SchoolId = schoolId,
                    Name = Input.Name.Trim(),
                    Barcode = string.IsNullOrWhiteSpace(Input.Barcode) ? null : Input.Barcode.Trim(),
                    CategoryId = Input.CategoryId,
                    Price = Input.Price,
                    Cost = Input.Cost,
                    MinStock = Input.MinStock,
                    CreatedAtUtc = DateTime.UtcNow,
                };
                _db.Products.Add(product);
                await _db.SaveChangesAsync();

                if (Input.InitialStock > 0)
                {
                    await _inventory.RegisterEntryAsync(
                        product.Id, Input.InitialStock, product.Cost, "Alta inicial", User.GetOperatorId());
                }

                Message = $"'{product.Name}' creado.";
            }
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleActiveAsync(Guid id)
    {
        try
        {
            var schoolId = User.GetSchoolId();
            var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id && p.SchoolId == schoolId);
            if (product is not null)
            {
                product.IsActive = !product.IsActive;
                await _db.SaveChangesAsync();
                Message = product.IsActive ? $"'{product.Name}' reactivado." : $"'{product.Name}' desactivado.";
            }
        }
        catch (Exception ex)
        {
            Error = $"No se pudo cambiar el estado del producto: {ex.Message}";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAdjustStockAsync(Guid id, decimal countedQuantity)
    {
        var schoolId = User.GetSchoolId();
        var product = await _db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id && p.SchoolId == schoolId);
        if (product is null)
        {
            Error = "Producto no encontrado.";
            return RedirectToPage();
        }

        try
        {
            await _inventory.AdjustToCountAsync(id, countedQuantity, "Ajuste desde el portal", User.GetOperatorId());
            Message = $"Existencia de '{product.Name}' ajustada a {countedQuantity:0.##}.";
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }

        return RedirectToPage();
    }

    private async Task LoadAsync(Guid schoolId)
    {
        var products = await _db.Products.Where(p => p.SchoolId == schoolId).ToListAsync();
        var categories = await _db.Categories.Where(c => c.SchoolId == schoolId).OrderBy(c => c.Name).ToListAsync();
        var catName = categories.ToDictionary(c => c.Id, c => c.Name);

        Categories = categories;
        ActiveCount = products.Count(p => p.IsActive);
        LowCount = products.Count(p => p.IsActive && p.StockOnHand <= p.MinStock);
        InventoryValue = products.Where(p => p.IsActive).Sum(p => p.StockOnHand * p.Cost);
        CategoryCount = catName.Count;

        string CatOf(Guid? id) => id.HasValue && catName.TryGetValue(id.Value, out var n) ? n : "Sin categoría";

        Rows = products
            .OrderBy(p => CatOf(p.CategoryId))
            .ThenBy(p => p.Name)
            .Select(p => new Row(p.Id, p.Name, CatOf(p.CategoryId), p.Price, p.StockOnHand, p.MinStock,
                p.IsActive, p.IsActive && p.StockOnHand <= p.MinStock))
            .ToList();
    }

    public sealed record Row(
        Guid Id, string Name, string Category, decimal Price, decimal Stock, decimal Min, bool Active, bool Low);
}
