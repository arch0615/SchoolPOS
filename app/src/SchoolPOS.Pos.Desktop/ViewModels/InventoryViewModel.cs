using System.Collections.ObjectModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolPOS.Data;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Pos.Desktop.Infrastructure;

namespace SchoolPOS.Pos.Desktop.ViewModels;

/// <summary>
/// Vista de inventario (FR-INV-1/5): catálogo con existencias, alerta de bajo inventario y
/// entrada rápida de mercancía (suma stock con asiento de Kardex).
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

    public InventoryViewModel(IServiceScopeFactory scopeFactory, PosSession session)
    {
        _scopeFactory = scopeFactory;
        _session = session;
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        RegisterEntryCommand = new AsyncRelayCommand(RegisterEntryAsync, () => Selected is not null && EntryQuantity > 0m);
    }

    public ObservableCollection<ProductRow> Products { get; } = new();

    public string Search { get => _search; set => SetProperty(ref _search, value); }

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
