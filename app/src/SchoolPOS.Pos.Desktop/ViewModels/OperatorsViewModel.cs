using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Enums;
using SchoolPOS.Pos.Desktop.Infrastructure;

namespace SchoolPOS.Pos.Desktop.ViewModels;

/// <summary>
/// Operadores del POS (FR-ADM-1): alta de cajeros y almacenistas, cambio de rol, baja, reinicio de
/// contraseña y desbloqueo. El asistente de instalación deja un solo administrador; sin esta
/// pantalla el modelo de roles existía pero la escuela no podía usarlo.
/// </summary>
public sealed class OperatorsViewModel : ViewModelBase, IAsyncLoadable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PosSession _session;

    private OperatorRow? _selected;
    private string _newUsername = string.Empty;
    private UserRole _newRole = UserRole.Cashier;
    private bool _includeInactive;
    private string _statusMessage = string.Empty;
    private string _errorMessage = string.Empty;

    public OperatorsViewModel(IServiceScopeFactory scopeFactory, PosSession session)
    {
        _scopeFactory = scopeFactory;
        _session = session;

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        CreateCommand = new AsyncRelayCommand(CreateAsync);
        SetRoleCommand = new AsyncRelayCommand(SetRoleAsync, () => Selected is not null);
        ToggleActiveCommand = new AsyncRelayCommand(ToggleActiveAsync, () => Selected is not null);
        ResetPasswordCommand = new AsyncRelayCommand(ResetPasswordAsync, () => Selected is not null);
        UnlockCommand = new AsyncRelayCommand(UnlockAsync, () => Selected is { IsLockedOut: true });
    }

    public ObservableCollection<OperatorRow> Operators { get; } = new();

    /// <summary>Roles asignables, para los desplegables.</summary>
    public IReadOnlyList<UserRole> Roles { get; } =
        new[] { UserRole.Cashier, UserRole.Warehouse, UserRole.Admin };

    public OperatorRow? Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value))
                return;
            if (value is not null)
                SelectedRole = value.Role;
            OnPropertyChanged(nameof(ToggleActiveText));
            SetRoleCommand.RaiseCanExecuteChanged();
            ToggleActiveCommand.RaiseCanExecuteChanged();
            ResetPasswordCommand.RaiseCanExecuteChanged();
            UnlockCommand.RaiseCanExecuteChanged();
        }
    }

    private UserRole _selectedRole = UserRole.Cashier;
    public UserRole SelectedRole { get => _selectedRole; set => SetProperty(ref _selectedRole, value); }

    public string ToggleActiveText => Selected is { IsActive: false } ? "Reactivar" : "Dar de baja";

    public string NewUsername { get => _newUsername; set => SetProperty(ref _newUsername, value); }
    public UserRole NewRole { get => _newRole; set => SetProperty(ref _newRole, value); }

    /// <summary>Contraseñas: asignadas desde el code-behind (PasswordBox no admite binding).</summary>
    public string NewPassword { private get; set; } = string.Empty;
    public string ResetPassword { private get; set; } = string.Empty;

    public bool IncludeInactive
    {
        get => _includeInactive;
        set { if (SetProperty(ref _includeInactive, value)) _ = LoadAsync(); }
    }

    public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }
    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand CreateCommand { get; }
    public AsyncRelayCommand SetRoleCommand { get; }
    public AsyncRelayCommand ToggleActiveCommand { get; }
    public AsyncRelayCommand ResetPasswordCommand { get; }
    public AsyncRelayCommand UnlockCommand { get; }

    public async Task LoadAsync()
    {
        ErrorMessage = string.Empty;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var registry = scope.ServiceProvider.GetRequiredService<IOperatorRegistry>();
            var rows = await registry.ListAsync(_session.SchoolId, IncludeInactive);

            Operators.Clear();
            foreach (var r in rows)
                Operators.Add(r);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"No se pudieron cargar los operadores: {ex.Message}";
        }
    }

    private async Task CreateAsync()
    {
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
            await auth.CreateOperatorAsync(_session.SchoolId, NewUsername.Trim(), NewPassword, NewRole);

            StatusMessage = $"Operador '{NewUsername.Trim()}' creado.";
            NewUsername = string.Empty;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private Task SetRoleAsync() => RunAsync(async registry =>
    {
        await registry.SetRoleAsync(Selected!.UserId, SelectedRole);
        StatusMessage = $"'{Selected.Username}' ahora es {RoleName(SelectedRole)}.";
    });

    private Task ToggleActiveAsync() => RunAsync(async registry =>
    {
        var current = Selected!;
        await registry.SetActiveAsync(current.UserId, !current.IsActive);
        StatusMessage = current.IsActive
            ? $"'{current.Username}' dado de baja."
            : $"'{current.Username}' reactivado.";
    });

    private Task ResetPasswordAsync() => RunAsync(async registry =>
    {
        await registry.ResetPasswordAsync(Selected!.UserId, ResetPassword);
        StatusMessage = $"Contraseña de '{Selected.Username}' actualizada. Entrégasela en persona.";
    });

    private Task UnlockAsync() => RunAsync(async registry =>
    {
        await registry.UnlockAsync(Selected!.UserId);
        StatusMessage = $"'{Selected.Username}' desbloqueado.";
    });

    /// <summary>Envoltura común: limpia mensajes, resuelve el servicio y recarga al terminar.</summary>
    private async Task RunAsync(Func<IOperatorRegistry, Task> action)
    {
        if (Selected is null)
            return;

        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            await action(scope.ServiceProvider.GetRequiredService<IOperatorRegistry>());
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    public static string RoleName(UserRole role) => role switch
    {
        UserRole.Admin => "Administrador",
        UserRole.Warehouse => "Almacén",
        _ => "Cajero",
    };
}
