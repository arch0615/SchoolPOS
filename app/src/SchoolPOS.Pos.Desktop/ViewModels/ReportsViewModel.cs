using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using SchoolPOS.Data.Reporting;
using SchoolPOS.Domain.Abstractions;
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

    private DateTime? _from = DateTime.UtcNow.Date;
    private DateTime? _to = DateTime.UtcNow.Date;
    private SalesSummary _sales = new(null, null, 0, 0, 0, 0);
    private CashFlowSummary _cashFlow = new(0, 0, 0, 0);
    private CustomerBalancesSummary _balances = new(0, 0);

    public ReportsViewModel(IServiceScopeFactory scopeFactory, PosSession session)
    {
        _scopeFactory = scopeFactory;
        _session = session;
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        ExportProductsCommand = new RelayCommand(ExportProducts, () => TopProducts.Count > 0);
    }

    public DateTime? From { get => _from; set => SetProperty(ref _from, value); }
    public DateTime? To { get => _to; set => SetProperty(ref _to, value); }

    public SalesSummary Sales { get => _sales; private set { SetProperty(ref _sales, value); OnPropertyChanged(nameof(SalesTotalText)); } }
    public CashFlowSummary CashFlow { get => _cashFlow; private set => SetProperty(ref _cashFlow, value); }
    public CustomerBalancesSummary Balances { get => _balances; private set => SetProperty(ref _balances, value); }

    public string SalesTotalText => Sales.Total.ToString("C2");

    public ObservableCollection<ProductSalesRow> TopProducts { get; } = new();

    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand ExportProductsCommand { get; }

    public async Task LoadAsync()
    {
        var fromUtc = From?.Date;
        var toUtc = To?.Date.AddDays(1).AddTicks(-1);

        using var scope = _scopeFactory.CreateScope();
        var salesReports = scope.ServiceProvider.GetRequiredService<ISalesReportService>();
        var finReports = scope.ServiceProvider.GetRequiredService<IFinancialReportService>();

        Sales = await salesReports.GetSummaryAsync(_session.SchoolId, fromUtc, toUtc);
        CashFlow = await finReports.GetCashFlowAsync(_session.SchoolId, fromUtc, toUtc);
        Balances = await finReports.GetCustomerBalancesAsync(_session.SchoolId);

        var byProduct = await salesReports.GetByProductAsync(_session.SchoolId, fromUtc, toUtc);
        TopProducts.Clear();
        foreach (var p in byProduct)
            TopProducts.Add(p);
        ExportProductsCommand.RaiseCanExecuteChanged();
    }

    private void ExportProducts()
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
    }
}
