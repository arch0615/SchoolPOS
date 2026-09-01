using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using SchoolPOS.Data.Reporting;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Common;
using SchoolPOS.Pos.Desktop.Infrastructure;

namespace SchoolPOS.Pos.Desktop.ViewModels;

/// <summary>
/// Reportes de ventas y financieros (FR-SAL-6, FR-TRE-4). Muestra resumen por periodo, top de
/// productos y flujo de efectivo; exporta el detalle a CSV.
/// </summary>
public sealed class ReportsViewModel : ViewModelBase, IAsyncLoadable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PosSession _session;

    private DateTime? _from = MxTime.TodayLocal(DateTime.UtcNow);
    private DateTime? _to = MxTime.TodayLocal(DateTime.UtcNow);
    private SalesSummary _sales = new(null, null, 0, 0, 0, 0);
    private CashFlowSummary _cashFlow = new(0, 0, 0, 0);
    private CustomerBalancesSummary _balances = new(0, 0);
    private string _errorMessage = string.Empty;
    private string _statusMessage = string.Empty;

    public ReportsViewModel(IServiceScopeFactory scopeFactory, PosSession session)
    {
        _scopeFactory = scopeFactory;
        _session = session;
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        ExportProductsCommand = new RelayCommand(ExportProducts, () => TopProducts.Count > 0);
        ExportPurchasesCommand = new RelayCommand(ExportPurchases, () => PurchasesBySupplier.Count > 0);
        ExportStudentSalesCommand = new RelayCommand(ExportStudentSales, () => SalesByStudent.Count > 0);
    }

    public DateTime? From { get => _from; set => SetProperty(ref _from, value); }
    public DateTime? To { get => _to; set => SetProperty(ref _to, value); }

    public SalesSummary Sales { get => _sales; private set { SetProperty(ref _sales, value); OnPropertyChanged(nameof(SalesTotalText)); } }
    public CashFlowSummary CashFlow { get => _cashFlow; private set => SetProperty(ref _cashFlow, value); }
    public CustomerBalancesSummary Balances { get => _balances; private set => SetProperty(ref _balances, value); }

    public string SalesTotalText => Sales.Total.ToString("C2");

    public ObservableCollection<ProductSalesRow> TopProducts { get; } = new();
    public ObservableCollection<SupplierPurchaseRow> PurchasesBySupplier { get; } = new();
    public ObservableCollection<StudentSalesRow> SalesByStudent { get; } = new();

    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }
    public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand ExportProductsCommand { get; }
    public RelayCommand ExportPurchasesCommand { get; }
    public RelayCommand ExportStudentSalesCommand { get; }

    public async Task LoadAsync()
    {
        ErrorMessage = string.Empty;
        try
        {
            // El operador elige días locales; las consultas van en UTC. Sin esta conversión el POS
            // y el portal reportaban cifras distintas del mismo día sobre los mismos datos.
            var fromUtc = MxTime.StartOfDayUtc(From);
            var toUtc = MxTime.EndOfDayUtc(To);

            using var scope = _scopeFactory.CreateScope();
            var salesReports = scope.ServiceProvider.GetRequiredService<ISalesReportService>();
            var finReports = scope.ServiceProvider.GetRequiredService<IFinancialReportService>();
            var purchasingReports = scope.ServiceProvider.GetRequiredService<IPurchasingReportService>();

            Sales = await salesReports.GetSummaryAsync(_session.SchoolId, fromUtc, toUtc);
            CashFlow = await finReports.GetCashFlowAsync(_session.SchoolId, fromUtc, toUtc);
            Balances = await finReports.GetCustomerBalancesAsync(_session.SchoolId);

            var byProduct = await salesReports.GetByProductAsync(_session.SchoolId, fromUtc, toUtc);
            TopProducts.Clear();
            foreach (var p in byProduct)
                TopProducts.Add(p);
            ExportProductsCommand.RaiseCanExecuteChanged();

            var bySupplier = await purchasingReports.GetBySupplierAsync(_session.SchoolId, fromUtc, toUtc);
            PurchasesBySupplier.Clear();
            foreach (var p in bySupplier)
                PurchasesBySupplier.Add(p);
            ExportPurchasesCommand.RaiseCanExecuteChanged();

            var byStudent = await salesReports.GetByStudentAsync(_session.SchoolId, fromUtc, toUtc);
            SalesByStudent.Clear();
            foreach (var s in byStudent)
                SalesByStudent.Add(s);
            ExportStudentSalesCommand.RaiseCanExecuteChanged();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"No se pudieron cargar los reportes: {ex.Message}";
        }
    }

    private void ExportProducts()
    {
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        try
        {
            var dialog = new SaveFileDialog
            {
                FileName = $"ventas_por_producto_{DateTime.UtcNow:yyyyMMdd}.csv",
                Filter = "CSV (*.csv)|*.csv",
            };
            if (dialog.ShowDialog() != true)
                return;

            var csv = Csv.Build(
                new[] { "Producto", "Cantidad", "Ingreso" },
                TopProducts.Select(p => new[] { p.Description, p.Quantity.ToString("0.##"), p.Revenue.ToString("0.00") }));
            File.WriteAllText(dialog.FileName, csv);
            StatusMessage = $"Exportado a {dialog.FileName}.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"No se pudo exportar el CSV: {ex.Message}";
        }
    }

    private void ExportPurchases()
    {
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        try
        {
            var dialog = new SaveFileDialog
            {
                FileName = $"compras_por_proveedor_{DateTime.UtcNow:yyyyMMdd}.csv",
                Filter = "CSV (*.csv)|*.csv",
            };
            if (dialog.ShowDialog() != true)
                return;

            var csv = Csv.Build(
                new[] { "Proveedor", "Órdenes", "Total" },
                PurchasesBySupplier.Select(p => new[] { p.SupplierName, p.OrderCount.ToString(), p.Total.ToString("0.00") }));
            File.WriteAllText(dialog.FileName, csv);
            StatusMessage = $"Exportado a {dialog.FileName}.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"No se pudo exportar el CSV: {ex.Message}";
        }
    }

    private void ExportStudentSales()
    {
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        try
        {
            var dialog = new SaveFileDialog
            {
                FileName = $"ventas_por_alumno_{DateTime.UtcNow:yyyyMMdd}.csv",
                Filter = "CSV (*.csv)|*.csv",
            };
            if (dialog.ShowDialog() != true)
                return;

            var csv = Csv.Build(
                new[] { "Alumno", "Ventas", "Total" },
                SalesByStudent.Select(s => new[] { s.StudentName, s.SaleCount.ToString(), s.Total.ToString("0.00") }));
            File.WriteAllText(dialog.FileName, csv);
            StatusMessage = $"Exportado a {dialog.FileName}.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"No se pudo exportar el CSV: {ex.Message}";
        }
    }
}
