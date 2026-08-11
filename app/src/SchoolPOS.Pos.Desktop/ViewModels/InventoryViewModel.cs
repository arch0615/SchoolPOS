using System.Collections.ObjectModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolPOS.Data;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Entities;
using SchoolPOS.Pos.Desktop.Infrastructure;

namespace SchoolPOS.Pos.Desktop.ViewModels;

/// <summary>
/// Vista de inventario (FR-INV-1/2/5): catálogo con existencias, alerta de bajo inventario,
/// alta de productos y categorías, y entrada rápida de mercancía (suma stock con asiento de
/// Kardex).
/// </summary>
public sealed class InventoryViewModel : ViewModelBase, IAsyncLoadable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PosSession _session;

    private string _search = string.Empty;
    private ProductRow? _selected;
    private decimal _entryQuantity = 1m;
    private string _statusMessage = string.Empty;
    private string _errorMessage = string.Empty;

    private string _newCategoryName = string.Empty;
    private string _newProductName = string.Empty;
    private string _newProductBarcode = string.Empty;
    private CategoryRow? _newProductCategory;
    private decimal _newProductPrice;
    private decimal _newProductCost;
    private decimal _newProductMinStock;

    public InventoryViewModel(IServiceScopeFactory scopeFactory, PosSession session)
    {
        _scopeFactory = scopeFactory;
        _session = session;
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        RegisterEntryCommand = new AsyncRelayCommand(RegisterEntryAsync, () => Selected is not null && EntryQuantity > 0m);
        AddCategoryCommand = new AsyncRelayCommand(AddCategoryAsync, () => NewCategoryName.Trim().Length > 0);
        CreateProductCommand = new AsyncRelayCommand(CreateProductAsync, () => NewProductName.Trim().Length > 0);
    }

    public ObservableCollection<ProductRow> Products { get; } = new();
    public ObservableCollection<CategoryRow> Categories { get; } = new();

    public string Search { get => _search; set => SetProperty(ref _search, value); }

    // ---- Categorías (FR-INV-2) ----
    public string NewCategoryName
    {
        get => _newCategoryName;
        set { if (SetProperty(ref _newCategoryName, value)) AddCategoryCommand.RaiseCanExecuteChanged(); }
    }

    public AsyncRelayCommand AddCategoryCommand { get; }

    // ---- Alta de producto ----
    public string NewProductName
    {
        get => _newProductName;
        set { if (SetProperty(ref _newProductName, value)) CreateProductCommand.RaiseCanExecuteChanged(); }
    }

    public string NewProductBarcode { get => _newProductBarcode; set => SetProperty(ref _newProductBarcode, value); }

    public CategoryRow? NewProductCategory { get => _newProductCategory; set => SetProperty(ref _newProductCategory, value); }

    public decimal NewProductPrice { get => _newProductPrice; set => SetProperty(ref _newProductPrice, value); }
    public decimal NewProductCost { get => _newProductCost; set => SetProperty(ref _newProductCost, value); }
    public decimal NewProductMinStock { get => _newProductMinStock; set => SetProperty(ref _newProductMinStock, value); }

    public AsyncRelayCommand CreateProductCommand { get; }

    public ProductRow? Selected
    {
        get => _selected;
        set { if (SetProperty(ref _selected, value)) RegisterEntryCommand.RaiseCanExecuteChanged(); }
    }

    public decimal EntryQuantity
    {
        get => _entryQuantity;
        set { if (SetProperty(ref _entryQuantity, value)) RegisterEntryCommand.RaiseCanExecuteChanged(); }
    }

    public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }
    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand RegisterEntryCommand { get; }

    public async Task LoadAsync()
    {
        ErrorMessage = string.Empty;
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();

        var categoryRows = await db.Categories.AsNoTracking()
            .Where(c => c.SchoolId == _session.SchoolId && c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new CategoryRow(c.Id, c.Name))
            .ToListAsync();

        var selectedCategoryId = NewProductCategory?.Id;
        Categories.Clear();
        foreach (var c in categoryRows)
            Categories.Add(c);
        NewProductCategory = Categories.FirstOrDefault(c => c.Id == selectedCategoryId);

        var query = db.Products.AsNoTracking().Where(p => p.SchoolId == _session.SchoolId && p.IsActive);
        if (!string.IsNullOrWhiteSpace(Search))
            query = query.Where(p => p.Name.Contains(Search) || (p.Barcode != null && p.Barcode.Contains(Search)));

        var rows = await query.OrderBy(p => p.Name)
            .Select(p => new ProductRow(p.Id, p.Name, p.Barcode, p.Price, p.StockOnHand, p.MinStock))
            .Take(200)
            .ToListAsync();

        Products.Clear();
        foreach (var r in rows)
            Products.Add(r);
    }

    private async Task AddCategoryAsync()
    {
        var name = NewCategoryName.Trim();
        if (name.Length == 0)
            return;

        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
            db.Categories.Add(new Category { SchoolId = _session.SchoolId, Name = name, CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();

            StatusMessage = $"Categoría '{name}' creada.";
            NewCategoryName = string.Empty;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"No se pudo crear la categoría: {ex.Message}";
        }
    }

    private async Task CreateProductAsync()
    {
        var name = NewProductName.Trim();
        if (name.Length == 0)
            return;

        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
            var product = new Product
            {
                SchoolId = _session.SchoolId,
                Name = name,
                Barcode = string.IsNullOrWhiteSpace(NewProductBarcode) ? null : NewProductBarcode.Trim(),
                CategoryId = NewProductCategory?.Id,
                Price = NewProductPrice,
                Cost = NewProductCost,
                MinStock = NewProductMinStock,
                CreatedAtUtc = DateTime.UtcNow,
            };
            db.Products.Add(product);
            await db.SaveChangesAsync();

            StatusMessage = $"'{name}' creado.";
            NewProductName = string.Empty;
            NewProductBarcode = string.Empty;
            NewProductPrice = 0m;
            NewProductCost = 0m;
            NewProductMinStock = 0m;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"No se pudo crear el producto: {ex.Message}";
        }
    }

    private async Task RegisterEntryAsync()
    {
        if (Selected is null || EntryQuantity <= 0m)
            return;

        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            await inventory.RegisterEntryAsync(
                Selected.Id, EntryQuantity, unitCost: null, reference: "Entrada manual", _session.Operator!.Id);

            StatusMessage = $"Entrada registrada: +{EntryQuantity} a {Selected.Name}.";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"No se pudo registrar la entrada: {ex.Message}";
        }
    }
}

/// <summary>Fila del catálogo de inventario.</summary>
public sealed record ProductRow(Guid Id, string Name, string? Barcode, decimal Price, decimal StockOnHand, decimal MinStock)
{
    public bool IsLow => StockOnHand <= MinStock;
}

/// <summary>Categoría para el selector de "Nuevo producto".</summary>
public sealed record CategoryRow(Guid Id, string Name);
