using SchoolPOS.Pos.Desktop.Infrastructure;

namespace SchoolPOS.Pos.Desktop.ViewModels;

/// <summary>
/// Shell principal del POS: cabecera con el operador y navegación por módulos según el rol
/// (control de acceso, FR-POS-1/2). Aloja la vista activa.
/// </summary>
public sealed class MainViewModel : ViewModelBase
{
    private readonly PosSession _session;
    private readonly DashboardViewModel _dashboard;
    private readonly SalesViewModel _sales;
    private readonly InventoryViewModel _inventory;
    private readonly PurchasingViewModel _purchasing;
    private readonly TreasuryViewModel _treasury;
    private readonly ReportsViewModel _reports;
    private readonly AuditViewModel _audit;
    private readonly SettingsViewModel _settings;

    private object? _currentView;

    public MainViewModel(
        PosSession session,
        DashboardViewModel dashboard,
        SalesViewModel sales,
        InventoryViewModel inventory,
        PurchasingViewModel purchasing,
        TreasuryViewModel treasury,
        ReportsViewModel reports,
        AuditViewModel audit,
        SettingsViewModel settings)
    {
        _session = session;
        _dashboard = dashboard;
        _sales = sales;
        _inventory = inventory;
        _purchasing = purchasing;
        _treasury = treasury;
        _reports = reports;
        _audit = audit;
        _settings = settings;

        ShowDashboardCommand = new RelayCommand(async () => await NavigateAsync(_dashboard));
        ShowSalesCommand = new RelayCommand(async () => await NavigateAsync(_sales));
        ShowInventoryCommand = new RelayCommand(async () => await NavigateAsync(_inventory), () => CanManageInventory);
        ShowPurchasingCommand = new RelayCommand(async () => await NavigateAsync(_purchasing), () => CanManagePurchasing);
        ShowTreasuryCommand = new RelayCommand(async () => await NavigateAsync(_treasury), () => CanManageTreasury);
        ShowReportsCommand = new RelayCommand(async () => await NavigateAsync(_reports), () => CanViewReports);
        ShowAuditCommand = new RelayCommand(async () => await NavigateAsync(_audit), () => CanViewReports);
        ShowSettingsCommand = new RelayCommand(async () => await NavigateAsync(_settings), () => CanManageSettings);
        SignOutCommand = new RelayCommand(() => SignOutRequested?.Invoke());

        _ = NavigateAsync(_dashboard);
    }

    public object? CurrentView
    {
        get => _currentView;
        private set => SetProperty(ref _currentView, value);
    }

    public string OperatorName => _session.Operator?.Username ?? "—";
    public string RoleName => _session.Role switch
    {
        Domain.Enums.UserRole.Admin => "Administrador",
        Domain.Enums.UserRole.Warehouse => "Almacén",
        _ => "Cajero",
    };

    public bool CanManageInventory => _session.CanManageInventory;
    public bool CanManagePurchasing => _session.CanManagePurchasing;
    public bool CanManageTreasury => _session.CanManageTreasury;
    public bool CanManageSettings => _session.CanManageSettings;
    public bool CanViewReports => _session.CanViewReports;

    public RelayCommand ShowDashboardCommand { get; }
    public RelayCommand ShowSalesCommand { get; }
    public RelayCommand ShowInventoryCommand { get; }
    public RelayCommand ShowPurchasingCommand { get; }
    public RelayCommand ShowTreasuryCommand { get; }
    public RelayCommand ShowReportsCommand { get; }
    public RelayCommand ShowAuditCommand { get; }
    public RelayCommand ShowSettingsCommand { get; }
    public RelayCommand SignOutCommand { get; }

    public event Action? SignOutRequested;

    private async Task NavigateAsync(object viewModel)
    {
        CurrentView = viewModel;
        if (viewModel is IAsyncLoadable loadable)
            await loadable.LoadAsync();
    }
}

/// <summary>View-models que cargan datos al mostrarse.</summary>
public interface IAsyncLoadable
{
    Task LoadAsync();
}
