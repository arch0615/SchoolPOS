using SchoolPOS.Pos.Desktop.Infrastructure;

namespace SchoolPOS.Pos.Desktop.ViewModels;

/// <summary>
/// Asistente de primer arranque. Sustituye lo que antes exigía editar <c>appsettings.json</c> a
/// mano y correr una herramienta de consola: la escuela elige modo, captura su nombre y su
/// administrador, y el asistente crea la base y guarda la configuración.
/// <para>
/// El modo "servidor de la escuela" (varias cajas contra un SQL Server) está previsto en la
/// pantalla pero todavía no implementado; se deja visible y deshabilitado para que la instalación
/// de una sola caja no se rediseñe cuando llegue.
/// </para>
/// </summary>
public sealed class SetupViewModel : ViewModelBase
{
    private string _schoolName = string.Empty;
    private string _adminUser = string.Empty;
    private string _errorMessage = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _isBusy;

    public SetupViewModel()
    {
        FinishCommand = new AsyncRelayCommand(FinishAsync, () => !IsBusy);
    }

    public string SchoolName
    {
        get => _schoolName;
        set { if (SetProperty(ref _schoolName, value)) FinishCommand.RaiseCanExecuteChanged(); }
    }

    public string AdminUser
    {
        get => _adminUser;
        set { if (SetProperty(ref _adminUser, value)) FinishCommand.RaiseCanExecuteChanged(); }
    }

    /// <summary>Contraseña (se asigna desde el code-behind: el PasswordBox no admite binding).</summary>
    public string AdminPassword { private get; set; } = string.Empty;
    public string AdminPasswordConfirm { private get; set; } = string.Empty;

    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }
    public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

    public bool IsBusy
    {
        get => _isBusy;
        set { if (SetProperty(ref _isBusy, value)) FinishCommand.RaiseCanExecuteChanged(); }
    }

    /// <summary>Dónde quedará la base, para que el usuario sepa qué respaldar.</summary>
    public string DatabasePathText => PosConfig.SqliteDatabasePath;

    public AsyncRelayCommand FinishCommand { get; }

    /// <summary>Se dispara cuando la caja quedó configurada y se puede continuar al acceso.</summary>
    public event Action? SetupCompleted;

    private async Task FinishAsync()
    {
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(SchoolName))
        {
            ErrorMessage = "Escriba el nombre de la escuela.";
            return;
        }
        if (string.IsNullOrWhiteSpace(AdminUser))
        {
            ErrorMessage = "Escriba el correo del administrador.";
            return;
        }
        if (AdminPassword.Length < 6)
        {
            ErrorMessage = "La contraseña debe tener al menos 6 caracteres.";
            return;
        }
        if (AdminPassword != AdminPasswordConfirm)
        {
            ErrorMessage = "Las contraseñas no coinciden.";
            return;
        }

        IsBusy = true;
        try
        {
            StatusMessage = "Preparando la base de datos…";
            System.IO.Directory.CreateDirectory(PosConfig.Directory);

            var provider = PosConfig.SqliteProvider;
            var connectionString = PosConfig.SqliteConnectionString;

            var failure = await PosProvisioner.TestAsync(provider, connectionString);
            if (failure is not null)
            {
                ErrorMessage = $"No se pudo crear la base de datos: {failure}";
                return;
            }

            StatusMessage = "Creando la escuela y el administrador…";
            var schoolId = await PosProvisioner.ProvisionAsync(
                provider, connectionString, SchoolName, AdminUser, AdminPassword);

            // La configuración se escribe al final: si algo falló antes, la caja sigue "sin
            // configurar" y el asistente vuelve a abrirse en el siguiente arranque.
            PosConfig.Save(schoolId, provider, connectionString);

            StatusMessage = "Listo.";
            SetupCompleted?.Invoke();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"No se pudo completar la configuración: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
