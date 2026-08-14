using System.Collections.ObjectModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolPOS.Data;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Entities;
using SchoolPOS.Domain.Enums;
using SchoolPOS.Pos.Desktop.Infrastructure;

namespace SchoolPOS.Pos.Desktop.ViewModels;

/// <summary>
/// Compras (FR-PUR): proveedores, órdenes de compra (con renglones) y su recepción — que
/// actualiza el inventario automáticamente vía <see cref="IPurchasingService"/> — y facturas de
/// proveedor con registro de pago. Solo Almacén/Administrador (mismo permiso que Inventario,
/// ya que la recepción alimenta directamente el stock).
/// </summary>
public sealed class PurchasingViewModel : ViewModelBase, IAsyncLoadable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PosSession _session;

    private string _statusMessage = string.Empty;
    private string _errorMessage = string.Empty;

    // ---- Proveedores ----
    private string _newSupplierName = string.Empty;
    private string _newSupplierRfc = string.Empty;
    private string _newSupplierContact = string.Empty;
    private string _newSupplierPhone = string.Empty;
    private string _newSupplierEmail = string.Empty;

    // ---- Nueva orden ----
    private SupplierRow? _orderSupplier;
    private string _orderNumber = string.Empty;
    private DateTime? _orderExpectedDate;
    private string _orderNotes = string.Empty;
    private ProductOption? _lineProduct;
    private decimal _lineQuantity = 1m;
    private decimal _lineUnitCost;

    private PurchaseOrderRow? _selectedOrder;

    // ---- Nueva factura ----
    private SupplierRow? _invoiceSupplier;
    private PurchaseOrderRow? _invoiceOrder;
    private string _invoiceNumber = string.Empty;
    private decimal _invoiceAmount;
    private DateTime _invoiceIssueDate = DateTime.Today;
    private DateTime? _invoiceDueDate;

    private InvoiceRow? _selectedInvoice;
    private decimal _paymentAmount;

    public PurchasingViewModel(IServiceScopeFactory scopeFactory, PosSession session)
    {
        _scopeFactory = scopeFactory;
        _session = session;

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        AddSupplierCommand = new AsyncRelayCommand(AddSupplierAsync, () => NewSupplierName.Trim().Length > 0);
        AddLineCommand = new RelayCommand(AddLine, () => LineProduct is not null && LineQuantity > 0m);
        RemoveLineCommand = new RelayCommand(RemoveSelectedLine, () => SelectedLine is not null);
        CreateOrderCommand = new AsyncRelayCommand(CreateOrderAsync,
            () => OrderSupplier is not null && OrderNumber.Trim().Length > 0 && OrderLines.Count > 0);
        MarkSentCommand = new AsyncRelayCommand(MarkSentAsync, () => SelectedOrder is { Status: PurchaseOrderStatus.Draft });
        ReceiveGoodsCommand = new AsyncRelayCommand(ReceiveGoodsAsync,
            () => SelectedOrder is { Status: PurchaseOrderStatus.Sent or PurchaseOrderStatus.PartiallyReceived });
        RegisterInvoiceCommand = new AsyncRelayCommand(RegisterInvoiceAsync,
            () => InvoiceSupplier is not null && InvoiceNumber.Trim().Length > 0 && InvoiceAmount > 0m);
        RegisterPaymentCommand = new AsyncRelayCommand(RegisterPaymentAsync,
            () => SelectedInvoice is not null && PaymentAmount > 0m);
    }

    public ObservableCollection<SupplierRow> Suppliers { get; } = new();
    public ObservableCollection<ProductOption> Products { get; } = new();
    public ObservableCollection<PurchaseOrderRow> Orders { get; } = new();
    public ObservableCollection<PoLineDraft> OrderLines { get; } = new();
    public ObservableCollection<InvoiceRow> Invoices { get; } = new();

    public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }
    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }

    // ---- Proveedores ----
    public string NewSupplierName
    {
        get => _newSupplierName;
        set { if (SetProperty(ref _newSupplierName, value)) AddSupplierCommand.RaiseCanExecuteChanged(); }
    }
    public string NewSupplierRfc { get => _newSupplierRfc; set => SetProperty(ref _newSupplierRfc, value); }
    public string NewSupplierContact { get => _newSupplierContact; set => SetProperty(ref _newSupplierContact, value); }
    public string NewSupplierPhone { get => _newSupplierPhone; set => SetProperty(ref _newSupplierPhone, value); }
    public string NewSupplierEmail { get => _newSupplierEmail; set => SetProperty(ref _newSupplierEmail, value); }
    public AsyncRelayCommand AddSupplierCommand { get; }

    // ---- Nueva orden ----
    public SupplierRow? OrderSupplier
    {
        get => _orderSupplier;
        set { if (SetProperty(ref _orderSupplier, value)) CreateOrderCommand.RaiseCanExecuteChanged(); }
    }

    public string OrderNumber
    {
        get => _orderNumber;
        set { if (SetProperty(ref _orderNumber, value)) CreateOrderCommand.RaiseCanExecuteChanged(); }
    }

    public DateTime? OrderExpectedDate { get => _orderExpectedDate; set => SetProperty(ref _orderExpectedDate, value); }
    public string OrderNotes { get => _orderNotes; set => SetProperty(ref _orderNotes, value); }

    public ProductOption? LineProduct
    {
        get => _lineProduct;
        set { if (SetProperty(ref _lineProduct, value)) AddLineCommand.RaiseCanExecuteChanged(); }
    }

    public decimal LineQuantity
    {
        get => _lineQuantity;
        set { if (SetProperty(ref _lineQuantity, value)) AddLineCommand.RaiseCanExecuteChanged(); }
    }

    public decimal LineUnitCost { get => _lineUnitCost; set => SetProperty(ref _lineUnitCost, value); }

    public decimal OrderLinesTotal => OrderLines.Sum(l => l.LineTotal);

    private PoLineDraft? _selectedLine;
    public PoLineDraft? SelectedLine
    {
        get => _selectedLine;
        set { if (SetProperty(ref _selectedLine, value)) RemoveLineCommand.RaiseCanExecuteChanged(); }
    }

    public AsyncRelayCommand CreateOrderCommand { get; }
    public RelayCommand AddLineCommand { get; }
    public RelayCommand RemoveLineCommand { get; }

    public PurchaseOrderRow? SelectedOrder
    {
        get => _selectedOrder;
        set
        {
            if (SetProperty(ref _selectedOrder, value))
            {
                MarkSentCommand.RaiseCanExecuteChanged();
                ReceiveGoodsCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public AsyncRelayCommand MarkSentCommand { get; }
    public AsyncRelayCommand ReceiveGoodsCommand { get; }

    // ---- Nueva factura ----
    public SupplierRow? InvoiceSupplier
    {
        get => _invoiceSupplier;
        set { if (SetProperty(ref _invoiceSupplier, value)) RegisterInvoiceCommand.RaiseCanExecuteChanged(); }
    }

    public PurchaseOrderRow? InvoiceOrder { get => _invoiceOrder; set => SetProperty(ref _invoiceOrder, value); }

    public string InvoiceNumber
    {
        get => _invoiceNumber;
        set { if (SetProperty(ref _invoiceNumber, value)) RegisterInvoiceCommand.RaiseCanExecuteChanged(); }
    }

    public decimal InvoiceAmount
    {
        get => _invoiceAmount;
        set { if (SetProperty(ref _invoiceAmount, value)) RegisterInvoiceCommand.RaiseCanExecuteChanged(); }
    }

    public DateTime InvoiceIssueDate { get => _invoiceIssueDate; set => SetProperty(ref _invoiceIssueDate, value); }
    public DateTime? InvoiceDueDate { get => _invoiceDueDate; set => SetProperty(ref _invoiceDueDate, value); }

    public AsyncRelayCommand RegisterInvoiceCommand { get; }

    // ---- Pago de factura ----
    public InvoiceRow? SelectedInvoice
    {
        get => _selectedInvoice;
        set { if (SetProperty(ref _selectedInvoice, value)) RegisterPaymentCommand.RaiseCanExecuteChanged(); }
    }

    public decimal PaymentAmount
    {
        get => _paymentAmount;
        set { if (SetProperty(ref _paymentAmount, value)) RegisterPaymentCommand.RaiseCanExecuteChanged(); }
    }

    public AsyncRelayCommand RegisterPaymentCommand { get; }

    public AsyncRelayCommand RefreshCommand { get; }

    public async Task LoadAsync()
    {
        ErrorMessage = string.Empty;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
            var schoolId = _session.SchoolId;

            var suppliers = await db.Suppliers.AsNoTracking()
                .Where(s => s.SchoolId == schoolId && s.IsActive)
                .OrderBy(s => s.Name)
                .Select(s => new SupplierRow(s.Id, s.Name))
                .ToListAsync();
            var selectedSupplierId = OrderSupplier?.Id;
            var invoiceSupplierId = InvoiceSupplier?.Id;
            Suppliers.Clear();
            foreach (var s in suppliers)
                Suppliers.Add(s);
            OrderSupplier = Suppliers.FirstOrDefault(s => s.Id == selectedSupplierId);
            InvoiceSupplier = Suppliers.FirstOrDefault(s => s.Id == invoiceSupplierId);

            var products = await db.Products.AsNoTracking()
                .Where(p => p.SchoolId == schoolId && p.IsActive)
                .OrderBy(p => p.Name)
                .Select(p => new ProductOption(p.Id, p.Name))
                .ToListAsync();
            Products.Clear();
            foreach (var p in products)
                Products.Add(p);

            var orders = await db.PurchaseOrders.AsNoTracking()
                .Where(o => o.SchoolId == schoolId)
                .OrderByDescending(o => o.OrderDate)
                .Select(o => new PurchaseOrderRow(o.Id, o.OrderNumber, o.Supplier.Name, o.Status, o.Total, o.OrderDate))
                .Take(200)
                .ToListAsync();
            var selectedOrderId = SelectedOrder?.Id;
            Orders.Clear();
            foreach (var o in orders)
                Orders.Add(o);
            SelectedOrder = Orders.FirstOrDefault(o => o.Id == selectedOrderId);

            var invoices = await db.SupplierInvoices.AsNoTracking()
                .Where(i => i.SchoolId == schoolId)
                .OrderByDescending(i => i.IssueDate)
                .Select(i => new InvoiceRow(i.Id, i.Supplier.Name, i.InvoiceNumber, i.Amount, i.AmountPaid, i.Status))
                .Take(200)
                .ToListAsync();
            Invoices.Clear();
            foreach (var i in invoices)
                Invoices.Add(i);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"No se pudieron cargar las compras: {ex.Message}";
        }
    }

    private async Task AddSupplierAsync()
    {
        var name = NewSupplierName.Trim();
        if (name.Length == 0)
            return;

        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
            db.Suppliers.Add(new Supplier
            {
                SchoolId = _session.SchoolId,
                Name = name,
                Rfc = string.IsNullOrWhiteSpace(NewSupplierRfc) ? null : NewSupplierRfc.Trim(),
                ContactName = string.IsNullOrWhiteSpace(NewSupplierContact) ? null : NewSupplierContact.Trim(),
                Phone = string.IsNullOrWhiteSpace(NewSupplierPhone) ? null : NewSupplierPhone.Trim(),
                Email = string.IsNullOrWhiteSpace(NewSupplierEmail) ? null : NewSupplierEmail.Trim(),
                CreatedAtUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();

            StatusMessage = $"Proveedor '{name}' creado.";
            NewSupplierName = string.Empty;
            NewSupplierRfc = string.Empty;
            NewSupplierContact = string.Empty;
            NewSupplierPhone = string.Empty;
            NewSupplierEmail = string.Empty;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"No se pudo crear el proveedor: {ex.Message}";
        }
    }

    private void AddLine()
    {
        if (LineProduct is null || LineQuantity <= 0m)
            return;

        var line = new PoLineDraft { ProductId = LineProduct.Id, ProductName = LineProduct.Name, Quantity = LineQuantity, UnitCost = LineUnitCost };
        line.Changed += () => OnPropertyChanged(nameof(OrderLinesTotal));
        OrderLines.Add(line);
        OnPropertyChanged(nameof(OrderLinesTotal));
        CreateOrderCommand.RaiseCanExecuteChanged();

        LineProduct = null;
        LineQuantity = 1m;
        LineUnitCost = 0m;
    }

    private void RemoveSelectedLine()
    {
        if (SelectedLine is null)
            return;
        OrderLines.Remove(SelectedLine);
        OnPropertyChanged(nameof(OrderLinesTotal));
        CreateOrderCommand.RaiseCanExecuteChanged();
    }

    private async Task CreateOrderAsync()
    {
        if (OrderSupplier is null || OrderNumber.Trim().Length == 0 || OrderLines.Count == 0)
            return;

        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var purchasing = scope.ServiceProvider.GetRequiredService<IPurchasingService>();
            var lines = OrderLines
                .Select(l => new PurchaseOrderLineRequest(l.ProductId, l.Quantity, l.UnitCost))
                .ToList();
            var order = await purchasing.CreateOrderAsync(
                _session.SchoolId, OrderSupplier.Id, OrderNumber.Trim(), lines, OrderExpectedDate,
                string.IsNullOrWhiteSpace(OrderNotes) ? null : OrderNotes.Trim());

            StatusMessage = $"Orden '{order.OrderNumber}' creada por {order.Total:C2}.";
            OrderNumber = string.Empty;
            OrderExpectedDate = null;
            OrderNotes = string.Empty;
            OrderLines.Clear();
            OnPropertyChanged(nameof(OrderLinesTotal));
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"No se pudo crear la orden: {ex.Message}";
        }
    }

    private async Task MarkSentAsync()
    {
        if (SelectedOrder is null)
            return;

        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var purchasing = scope.ServiceProvider.GetRequiredService<IPurchasingService>();
            await purchasing.MarkSentAsync(SelectedOrder.Id);
            StatusMessage = $"Orden '{SelectedOrder.OrderNumber}' marcada como enviada.";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"No se pudo marcar la orden como enviada: {ex.Message}";
        }
    }

    /// <summary>
    /// Recibe el saldo pendiente completo de todos los renglones de la orden seleccionada (no
    /// admite recepción parcial línea por línea desde esta pantalla — para eso, usar recepciones
    /// sucesivas: cada llamada recibe lo que aún falte).
    /// </summary>
    private async Task ReceiveGoodsAsync()
    {
        if (SelectedOrder is null)
            return;

        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
            var order = await db.PurchaseOrders.AsNoTracking()
                .Include(o => o.Lines)
                .FirstOrDefaultAsync(o => o.Id == SelectedOrder.Id)
                ?? throw new InvalidOperationException("Orden no encontrada.");

            var pending = order.Lines
                .Where(l => l.Quantity - l.QuantityReceived > 0m)
                .Select(l => new ReceiptLineRequest(l.ProductId, l.Quantity - l.QuantityReceived, l.UnitCost, l.Id))
                .ToList();

            if (pending.Count == 0)
            {
                ErrorMessage = "Esta orden ya no tiene renglones pendientes de recibir.";
                return;
            }

            var purchasing = scope.ServiceProvider.GetRequiredService<IPurchasingService>();
            await purchasing.ReceiveGoodsAsync(order.Id, pending, _session.Operator!.Id, notes: "Recepción desde el POS");

            StatusMessage = $"Recepción registrada para la orden '{order.OrderNumber}'. Inventario actualizado.";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"No se pudo recibir la mercancía: {ex.Message}";
        }
    }

    private async Task RegisterInvoiceAsync()
    {
        if (InvoiceSupplier is null || InvoiceNumber.Trim().Length == 0 || InvoiceAmount <= 0m)
            return;

        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var purchasing = scope.ServiceProvider.GetRequiredService<IPurchasingService>();
            await purchasing.RegisterInvoiceAsync(
                _session.SchoolId, InvoiceSupplier.Id, InvoiceOrder?.Id, InvoiceNumber.Trim(), InvoiceAmount,
                InvoiceIssueDate, InvoiceDueDate);

            StatusMessage = $"Factura '{InvoiceNumber.Trim()}' registrada por {InvoiceAmount:C2}.";
            InvoiceNumber = string.Empty;
            InvoiceAmount = 0m;
            InvoiceDueDate = null;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"No se pudo registrar la factura: {ex.Message}";
        }
    }

    private async Task RegisterPaymentAsync()
    {
        if (SelectedInvoice is null || PaymentAmount <= 0m)
            return;

        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var purchasing = scope.ServiceProvider.GetRequiredService<IPurchasingService>();
            await purchasing.RegisterInvoicePaymentAsync(SelectedInvoice.Id, PaymentAmount);
            StatusMessage = $"Pago de {PaymentAmount:C2} aplicado a la factura '{SelectedInvoice.InvoiceNumber}'.";
            PaymentAmount = 0m;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"No se pudo registrar el pago: {ex.Message}";
        }
    }
}

/// <summary>Renglón en edición al armar una orden de compra (analogía de <see cref="CartLine"/>).</summary>
public sealed class PoLineDraft : ViewModelBase
{
    private decimal _quantity = 1m;
    private decimal _unitCost;

    public required Guid ProductId { get; init; }
    public required string ProductName { get; init; }

    public decimal Quantity
    {
        get => _quantity;
        set { if (SetProperty(ref _quantity, value)) NotifyTotal(); }
    }

    public decimal UnitCost
    {
        get => _unitCost;
        set { if (SetProperty(ref _unitCost, value)) NotifyTotal(); }
    }

    public decimal LineTotal => Quantity * UnitCost;

    public event Action? Changed;

    private void NotifyTotal()
    {
        OnPropertyChanged(nameof(LineTotal));
        Changed?.Invoke();
    }
}

public sealed record SupplierRow(Guid Id, string Name);
public sealed record ProductOption(Guid Id, string Name);
public sealed record PurchaseOrderRow(Guid Id, string OrderNumber, string SupplierName, PurchaseOrderStatus Status, decimal Total, DateTime OrderDate);
public sealed record InvoiceRow(Guid Id, string SupplierName, string InvoiceNumber, decimal Amount, decimal AmountPaid, SupplierInvoiceStatus Status)
{
    public decimal Balance => Amount - AmountPaid;
}
