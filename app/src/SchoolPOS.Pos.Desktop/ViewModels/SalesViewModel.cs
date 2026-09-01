using System.Collections.ObjectModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolPOS.Data;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Enums;
using SchoolPOS.Domain.Exceptions;
using SchoolPOS.Pos.Desktop.Infrastructure;

namespace SchoolPOS.Pos.Desktop.ViewModels;

/// <summary>
/// Pantalla de venta/cobro (núcleo del POS, FR-SAL-1..7). Agrega productos por código de barras o
/// búsqueda, identifica al estudiante para cobrar contra su saldo o en efectivo, y registra la
/// venta de forma atómica (descuenta stock y saldo en una sola transacción). Opera contra la DB
/// local, por lo que funciona sin internet (NFR-2).
/// </summary>
public sealed class SalesViewModel : ViewModelBase, IAsyncLoadable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PosSession _session;

    private string _productCode = string.Empty;
    private string _studentCode = string.Empty;
    private StudentBalance? _currentStudent;
    private bool _isBalanceTender = true;
    private decimal? _amountReceived;
    private string _statusMessage = string.Empty;
    private string _errorMessage = string.Empty;
    private Guid? _cashSessionId;

    public SalesViewModel(IServiceScopeFactory scopeFactory, PosSession session)
    {
        _scopeFactory = scopeFactory;
        _session = session;

        Cart.CollectionChanged += (_, _) => RecalculateTotals();

        AddProductCommand = new AsyncRelayCommand(AddProductAsync);
        RemoveLineCommand = new RelayCommand(RemoveSelectedLine, () => SelectedLine is not null);
        IdentifyStudentCommand = new AsyncRelayCommand(IdentifyStudentAsync);
        ClearStudentCommand = new RelayCommand(ClearStudent);
        ChargeCommand = new AsyncRelayCommand(ChargeAsync, CanCharge);
        NewSaleCommand = new RelayCommand(ResetSale);
    }

    // ---- Carrito ----
    public ObservableCollection<CartLine> Cart { get; } = new();

    private CartLine? _selectedLine;
    public CartLine? SelectedLine
    {
        get => _selectedLine;
        set { if (SetProperty(ref _selectedLine, value)) RemoveLineCommand.RaiseCanExecuteChanged(); }
    }

    public string ProductCode { get => _productCode; set => SetProperty(ref _productCode, value); }

    public decimal Subtotal => Cart.Sum(l => Math.Round(l.Quantity * l.UnitPrice, 2, MidpointRounding.AwayFromZero));
    public decimal DiscountTotal => Cart.Sum(l => l.Discount);
    public decimal Total => Cart.Sum(l => l.LineTotal);

    // ---- Cliente ----
    public string StudentCode { get => _studentCode; set => SetProperty(ref _studentCode, value); }

    public StudentBalance? CurrentStudent
    {
        get => _currentStudent;
        private set
        {
            if (SetProperty(ref _currentStudent, value))
            {
                OnPropertyChanged(nameof(StudentName));
                OnPropertyChanged(nameof(StudentBalanceText));
                OnPropertyChanged(nameof(HasStudent));
                ChargeCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasStudent => CurrentStudent is not null;
    public string StudentName => CurrentStudent?.FullName ?? "Sin identificar";
    public string StudentBalanceText => CurrentStudent is null ? "—" : CurrentStudent.Balance.ToString("C2");

    // ---- Cobro ----
    public bool IsBalanceTender
    {
        get => _isBalanceTender;
        set
        {
            if (SetProperty(ref _isBalanceTender, value))
            {
                OnPropertyChanged(nameof(IsCashTender));
                OnPropertyChanged(nameof(Change));
                OnPropertyChanged(nameof(IsAmountInsufficient));
                OnPropertyChanged(nameof(NeedsOpenTill));
                ChargeCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsCashTender
    {
        get => !_isBalanceTender;
        set => IsBalanceTender = !value;
    }

    public decimal? AmountReceived
    {
        get => _amountReceived;
        set
        {
            if (SetProperty(ref _amountReceived, value))
            {
                OnPropertyChanged(nameof(Change));
                OnPropertyChanged(nameof(IsAmountInsufficient));
                ChargeCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public decimal Change => IsCashTender && AmountReceived is { } received ? Math.Max(0m, received - Total) : 0m;

    /// <summary>Aviso visible: el efectivo capturado no alcanza para cubrir el total (o está vacío).</summary>
    public bool IsAmountInsufficient =>
        IsCashTender && Total > 0m && !(AmountReceived is { } received && received >= Total);

    /// <summary>
    /// Caja abierta del operador. Toda venta en efectivo se amarra a ella: es lo que permite que el
    /// arqueo compare el efectivo contado contra lo que debería haber. Sin caja abierta, las ventas
    /// en efectivo quedaban fuera del cálculo y aparecían como sobrante inexplicado al cerrar.
    /// </summary>
    public bool HasOpenTill => _cashSessionId is not null;

    /// <summary>Aviso visible cuando falta abrir la caja y el cobro es en efectivo.</summary>
    public bool NeedsOpenTill => IsCashTender && !HasOpenTill;

    /// <summary>Descuentos permitidos solo con rol autorizado (FR-SAL-3).</summary>
    public bool CanApplyDiscount => _session.CanApplyDiscount;
    public bool IsDiscountReadOnly => !CanApplyDiscount;

    public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }
    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }

    public AsyncRelayCommand AddProductCommand { get; }
    public RelayCommand RemoveLineCommand { get; }
    public AsyncRelayCommand IdentifyStudentCommand { get; }
    public RelayCommand ClearStudentCommand { get; }
    public AsyncRelayCommand ChargeCommand { get; }
    public RelayCommand NewSaleCommand { get; }

    public async Task LoadAsync()
    {
        ResetSale();
        await RefreshOpenTillAsync();
    }

    /// <summary>Relee la caja abierta del operador (puede haberla abierto en Tesorería hace un momento).</summary>
    private async Task RefreshOpenTillAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
            var operatorId = _session.Operator!.Id;

            _cashSessionId = await db.CashSessions.AsNoTracking()
                .Where(s => s.SchoolId == _session.SchoolId
                            && s.OperatorId == operatorId
                            && s.Status == CashSessionStatus.Open)
                .OrderByDescending(s => s.OpenedAtUtc)
                .Select(s => (Guid?)s.Id)
                .FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            _cashSessionId = null;
            ErrorMessage = $"No se pudo consultar la caja abierta: {ex.Message}";
        }

        OnPropertyChanged(nameof(HasOpenTill));
        OnPropertyChanged(nameof(NeedsOpenTill));
        ChargeCommand.RaiseCanExecuteChanged();
    }

    private async Task AddProductAsync()
    {
        ErrorMessage = string.Empty;
        var code = ProductCode.Trim();
        if (code.Length == 0)
            return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            var product = await inventory.FindByBarcodeAsync(_session.SchoolId, code);

            if (product is null)
            {
                ErrorMessage = $"Producto no encontrado: {code}";
                return;
            }

            var existing = Cart.FirstOrDefault(l => l.ProductId == product.Id);
            if (existing is not null)
            {
                existing.Quantity += 1m;
            }
            else
            {
                var line = new CartLine { ProductId = product.Id, Description = product.Name, UnitPrice = product.Price };
                line.Changed += RecalculateTotals;
                Cart.Add(line);
            }

            ProductCode = string.Empty;
            RecalculateTotals();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"No se pudo agregar el producto: {ex.Message}";
        }
    }

    private void RemoveSelectedLine()
    {
        if (SelectedLine is null)
            return;
        SelectedLine.Changed -= RecalculateTotals;
        Cart.Remove(SelectedLine);
        RecalculateTotals();
    }

    private async Task IdentifyStudentAsync()
    {
        ErrorMessage = string.Empty;
        var code = StudentCode.Trim();
        if (code.Length == 0)
            return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var directory = scope.ServiceProvider.GetRequiredService<IStudentDirectory>();
            var student = await directory.FindByCodeAsync(_session.SchoolId, code);

            if (student is null)
            {
                ErrorMessage = $"Estudiante no encontrado: {code}";
                CurrentStudent = null;
                return;
            }

            CurrentStudent = student;
            StudentCode = string.Empty;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"No se pudo identificar al estudiante: {ex.Message}";
        }
    }

    private void ClearStudent()
    {
        CurrentStudent = null;
        StudentCode = string.Empty;
    }

    private bool CanCharge()
    {
        if (Cart.Count == 0)
            return false;
        if (IsBalanceTender && CurrentStudent is null)
            return false;
        // Sin esto, un cajero podía cobrar un total mayor al efectivo realmente recibido (o dejar el
        // campo vacío) y la venta se registraba igual como pagada por completo — el dinero que
        // faltaba nunca quedaba en ningún lado. El botón se deshabilita hasta que alcance.
        if (IsCashTender && !(AmountReceived is { } received && received >= Total))
            return false;
        return true;
    }

    private async Task ChargeAsync()
    {
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;

        if (Cart.Count == 0)
        {
            ErrorMessage = "El carrito está vacío.";
            return;
        }
        if (IsBalanceTender && CurrentStudent is null)
        {
            ErrorMessage = "Identifique al estudiante para cobrar por saldo.";
            return;
        }
        if (IsCashTender && !(AmountReceived is { } receivedCheck && receivedCheck >= Total))
        {
            ErrorMessage = "El efectivo recibido debe cubrir el total de la venta.";
            return;
        }

        // Relectura justo antes de cobrar: el cajero pudo abrir su caja en Tesorería sin volver a
        // pasar por aquí, y así no pierde el carrito que ya armó.
        if (IsCashTender && !HasOpenTill)
            await RefreshOpenTillAsync();

        if (IsCashTender && !HasOpenTill)
        {
            ErrorMessage = "Abra su caja en Tesorería antes de cobrar en efectivo.";
            return;
        }

        var lines = Cart.Select(l => new SaleLineRequest(l.ProductId, l.Description, l.Quantity, l.UnitPrice, l.Discount)).ToList();
        var request = new SaleRequest(
            _session.SchoolId,
            _session.Operator!.Id,
            IsBalanceTender ? TenderType.Balance : TenderType.Cash,
            lines,
            StudentId: CurrentStudent?.StudentId,
            AccountId: CurrentStudent?.AccountId,
            // Solo las ventas en efectivo entran al arqueo; las de saldo no mueven el cajón.
            CashSessionId: IsCashTender ? _cashSessionId : null,
            AmountTendered: IsCashTender ? AmountReceived : null);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var sales = scope.ServiceProvider.GetRequiredService<ISalesService>();
            var sale = await sales.RegisterSaleAsync(request);

            StatusMessage = IsCashTender
                ? $"Venta registrada: {sale.Total:C2}. Cambio: {Change:C2}"
                : $"Venta registrada: {sale.Total:C2} cargada al saldo de {StudentName}.";
            ResetSale();
        }
        catch (InsufficientBalanceException)
        {
            ErrorMessage = "Saldo insuficiente del estudiante para esta venta.";
        }
        catch (InsufficientStockException ex)
        {
            ErrorMessage = $"Sin existencias suficientes (producto {ex.ProductId}).";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"No se pudo registrar la venta: {ex.Message}";
        }
    }

    private void ResetSale()
    {
        foreach (var line in Cart)
            line.Changed -= RecalculateTotals;
        Cart.Clear();
        CurrentStudent = null;
        StudentCode = string.Empty;
        ProductCode = string.Empty;
        AmountReceived = null;
        IsBalanceTender = true;
        RecalculateTotals();
    }

    private void RecalculateTotals()
    {
        OnPropertyChanged(nameof(Subtotal));
        OnPropertyChanged(nameof(DiscountTotal));
        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(Change));
        OnPropertyChanged(nameof(IsAmountInsufficient));
        ChargeCommand.RaiseCanExecuteChanged();
    }
}
