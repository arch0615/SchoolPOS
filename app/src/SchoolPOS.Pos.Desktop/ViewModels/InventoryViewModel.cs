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
        SaveProductCommand = new AsyncRelayCommand(SaveProductAsync, () => NewProductName.Trim().Length > 0);
        NewProductCommand = new RelayCommand(ClearProductForm);
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
        set { if (SetProperty(ref _newProductName, value)) SaveProductCommand.RaiseCanExecuteChanged(); }
    }

    public string NewProductBarcode { get => _newProductBarcode; set => SetProperty(ref _newProductBarcode, value); }

    public CategoryRow? NewProductCategory { get => _newProductCategory; set => SetProperty(ref _newProductCategory, value); }

    public decimal NewProductPrice { get => _newProductPrice; set => SetProperty(ref _newProductPrice, value); }
    public decimal NewProductCost { get => _newProductCost; set => SetProperty(ref _newProductCost, value); }
    public decimal NewProductMinStock { get => _newProductMinStock; set => SetProperty(ref _newProductMinStock, value); }

    /// <summary>Crea si no hay producto seleccionado; guarda cambios sobre el seleccionado si lo hay.</summary>
    public AsyncRelayCommand SaveProductCommand { get; }

    /// <summary>Limpia el formulario y quita la selección, para volver a modo "crear".</summary>
    public RelayCommand NewProductCommand { get; }

    public bool IsEditingProduct => Selected is not null;
    public string SaveProductButtonText => IsEditingProduct ? "Guardar cambios" : "Crear producto";

    public ProductRow? Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value))
                return;

            // Igual que en Alumnos: elegir un producto pasa el formulario a modo edición con sus
            // datos actuales, en vez de dejarlo como un formulario de alta ciego a lo ya existente.
            if (value is not null)
            {
                NewProductName = value.Name;
                NewProductBarcode = value.Barcode ?? string.Empty;
                NewProductCategory = Categories.FirstOrDefault(c => c.Id == value.CategoryId);
                NewProductPrice = value.Price;
                NewProductCost = value.Cost;
                NewProductMinStock = value.MinStock;
            }

            RegisterEntryCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(IsEditingProduct));
            OnPropertyChanged(nameof(SaveProductButtonText));
        }
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
        try
        {
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
            {
                // En minúsculas: SQLite distingue mayúsculas en Contains() y SQL Server no, así
                // que sin esto la búsqueda se comporta distinto según el modo de instalación.
                var needle = Search.Trim().ToLower();
                query = query.Where(p => p.Name.ToLower().Contains(needle) || (p.Barcode != null && p.Barcode.ToLower().Contains(needle)));
            }

            var rows = await query.OrderBy(p => p.Name)
                .Select(p => new ProductRow(p.Id, p.Name, p.Barcode, p.Price, p.Cost, p.StockOnHand, p.MinStock, p.CategoryId))
                .Take(200)
                .ToListAsync();

            var selectedId = Selected?.Id;
            Products.Clear();
            foreach (var r in rows)
                Products.Add(r);
            // Recupera la selección tras recargar (Guardar cambios llama a LoadAsync): si no se
            // vuelve a seleccionar, el usuario pierde el modo edición justo después de guardar.
            Selected = Products.FirstOrDefault(p => p.Id == selectedId);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"No se pudo cargar el inventario: {ex.Message}";
        }
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

    private async Task SaveProductAsync()
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
            var barcode = string.IsNullOrWhiteSpace(NewProductBarcode) ? null : NewProductBarcode.Trim();

            if (Selected is { } editing)
            {
                // Editar uno existente: antes no había forma de corregir un precio equivocado sin
                // tocar la base de datos a mano. No toca StockOnHand — eso solo se mueve por
                // Entrada/Salida/Ajuste, con su propio asiento de Kardex.
                var product = await db.Products.FirstOrDefaultAsync(p => p.Id == editing.Id)
                    ?? throw new InvalidOperationException("El producto ya no existe.");
                var oldPrice = product.Price;
                product.Name = name;
                product.Barcode = barcode;
                product.CategoryId = NewProductCategory?.Id;
                product.Price = NewProductPrice;
                product.Cost = NewProductCost;
                product.MinStock = NewProductMinStock;

                // Bitácora: un precio equivocado corregido en silencio no deja rastro de qué era
                // antes ni quién lo cambió (FR-ADM-4).
                if (oldPrice != product.Price)
                {
                    db.AuditLogs.Add(new AuditLog
                    {
                        SchoolId = _session.SchoolId,
                        Actor = _session.Operator!.Id.ToString(),
                        Action = "PriceChange",
                        Entity = nameof(Product),
                        EntityId = product.Id.ToString(),
                        Before = oldPrice.ToString("0.00"),
                        After = product.Price.ToString("0.00"),
                        CreatedAtUtc = DateTime.UtcNow,
                    });
                }

                await db.SaveChangesAsync();

                StatusMessage = $"'{name}' actualizado.";
            }
            else
            {
                var product = new Product
                {
                    SchoolId = _session.SchoolId,
                    Name = name,
                    Barcode = barcode,
                    CategoryId = NewProductCategory?.Id,
                    Price = NewProductPrice,
                    Cost = NewProductCost,
                    MinStock = NewProductMinStock,
                    CreatedAtUtc = DateTime.UtcNow,
                };
                db.Products.Add(product);
                await db.SaveChangesAsync();

                StatusMessage = $"'{name}' creado.";
                ClearProductForm();
            }

            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"No se pudo guardar el producto: {ex.Message}";
        }
    }

    private void ClearProductForm()
    {
        Selected = null;
        NewProductName = string.Empty;
        NewProductBarcode = string.Empty;
        NewProductCategory = null;
        NewProductPrice = 0m;
        NewProductCost = 0m;
        NewProductMinStock = 0m;
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
public sealed record ProductRow(
    Guid Id, string Name, string? Barcode, decimal Price, decimal Cost, decimal StockOnHand,
    decimal MinStock, Guid? CategoryId)
{
    public bool IsLow => StockOnHand <= MinStock;
}

/// <summary>Categoría para el selector de "Nuevo producto".</summary>
public sealed record CategoryRow(Guid Id, string Name);
