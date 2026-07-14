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

    private object? _currentView;

    public MainViewModel(
        PosSession session,
        DashboardViewModel dashboard,
        SalesViewModel sales,
        InventoryViewModel inventory)
    {
        _session = session;
        _dashboard = dashboard;
        _sales = sales;
        _inventory = inventory;

        ShowDashboardCommand = new RelayCommand(async () => await NavigateAsync(_dashboard));
        ShowSalesCommand = new RelayCommand(async () => await NavigateAsync(_sales));
        ShowInventoryCommand = new RelayCommand(async () => await NavigateAsync(_inventory), () => CanManageInventory);
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

    public RelayCommand ShowDashboardCommand { get; }
    public RelayCommand ShowSalesCommand { get; }
    public RelayCommand ShowInventoryCommand { get; }
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
