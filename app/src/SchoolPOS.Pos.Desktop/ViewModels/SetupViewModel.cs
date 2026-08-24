using SchoolPOS.Pos.Desktop.Infrastructure;

namespace SchoolPOS.Pos.Desktop.ViewModels;

/// <summary>
/// Asistente de primer arranque. Sustituye lo que antes exigía editar <c>appsettings.json</c> a
/// mano y correr una herramienta de consola: la escuela elige modo, captura su nombre y su
/// administrador, y el asistente crea la base y guarda la configuración.
/// <para>
/// El modo "servidor de la escuela" apunta a un SQL Server compartido por varias cajas. No crea
/// una segunda escuela si ya existe una en esa base: <see cref="PosProvisioner.ProvisionAsync"/>
/// reutiliza la escuela existente y solo agrega el operador capturado aquí — así, correr el
/// asistente en la segunda caja de una escuela simplemente "se une" a la primera.
/// </para>
/// </summary>
public sealed class SetupViewModel : ViewModelBase
{
    private string _schoolName = string.Empty;
    private string _adminUser = string.Empty;
    private string _errorMessage = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _isBusy;

    private bool _isServerMode;
    private string _serverAddress = string.Empty;
    private string _databaseName = "SchoolPOS";
    private bool _useSqlAuth;
    private string _sqlUsername = string.Empty;

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

    /// <summary>Falso = única caja (SQLite local); verdadero = servidor compartido (SQL Server).</summary>
    public bool IsServerMode
    {
        get => _isServerMode;
        set
        {
            if (SetProperty(ref _isServerMode, value))
            {
                OnPropertyChanged(nameof(DatabasePathText));
                FinishCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ServerAddress
    {
        get => _serverAddress;
        set { if (SetProperty(ref _serverAddress, value)) OnPropertyChanged(nameof(DatabasePathText)); }
    }

    public string DatabaseName
    {
        get => _databaseName;
        set { if (SetProperty(ref _databaseName, value)) OnPropertyChanged(nameof(DatabasePathText)); }
    }

    /// <summary>
    /// Falso (por defecto) = autenticación de Windows: no hay contraseña de SQL que administrar,
    /// sirve si el servicio del POS corre con una cuenta que ya tiene acceso a la base. Verdadero =
    /// usuario y contraseña de SQL Server, para servidores sin dominio o con acceso restringido.
    /// </summary>
    public bool UseSqlAuth { get => _useSqlAuth; set => SetProperty(ref _useSqlAuth, value); }

    public string SqlUsername { get => _sqlUsername; set => SetProperty(ref _sqlUsername, value); }

    /// <summary>Contraseña de SQL (se asigna desde el code-behind, igual que <see cref="AdminPassword"/>).</summary>
    public string SqlPassword { private get; set; } = string.Empty;

    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }
    public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

    public bool IsBusy
    {
        get => _isBusy;
        set { if (SetProperty(ref _isBusy, value)) FinishCommand.RaiseCanExecuteChanged(); }
    }

    /// <summary>Dónde quedará la base, para que el usuario sepa qué respaldar.</summary>
    public string DatabasePathText => IsServerMode
        ? (string.IsNullOrWhiteSpace(ServerAddress) || string.IsNullOrWhiteSpace(DatabaseName)
            ? "En el servidor que indiques abajo."
            : $"Servidor {ServerAddress}, base \"{DatabaseName}\". Ya no vive en esta computadora.")
        : PosConfig.SqliteDatabasePath;

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

        string provider;
        string connectionString;

        if (IsServerMode)
        {
            if (string.IsNullOrWhiteSpace(ServerAddress))
            {
                ErrorMessage = "Escriba la dirección del servidor.";
                return;
            }
            if (string.IsNullOrWhiteSpace(DatabaseName))
            {
                ErrorMessage = "Escriba el nombre de la base de datos.";
                return;
            }
            if (UseSqlAuth && string.IsNullOrWhiteSpace(SqlUsername))
            {
                ErrorMessage = "Escriba el usuario de SQL Server.";
                return;
            }

            provider = PosConfig.SqlServerProvider;
            connectionString = BuildSqlServerConnectionString();
        }
        else
        {
            provider = PosConfig.SqliteProvider;
            connectionString = PosConfig.SqliteConnectionString;
        }

        IsBusy = true;
        try
        {
            StatusMessage = "Preparando la base de datos…";
            System.IO.Directory.CreateDirectory(PosConfig.Directory);

            var failure = await PosProvisioner.TestAsync(provider, connectionString);
            if (failure is not null)
            {
                ErrorMessage = $"No se pudo {(IsServerMode ? "conectar al servidor" : "crear la base de datos")}: {failure}";
                return;
            }

            StatusMessage = IsServerMode
                ? "Conectando con la escuela en el servidor…"
                : "Creando la escuela y el administrador…";
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

    private string BuildSqlServerConnectionString()
    {
        var server = ServerAddress.Trim();
        var database = DatabaseName.Trim();

        // TrustServerCertificate: los SQL Server de escuela normalmente corren sin un certificado
        // TLS firmado por una CA pública; sin esto, Microsoft.Data.SqlClient (que valida el
        // certificado por defecto desde la v18) rechaza la conexión aunque el usuario/contraseña
        // sean correctos, con un error de TLS que no dice nada sobre el problema real.
        return UseSqlAuth
            ? $"Server={server};Database={database};User Id={SqlUsername.Trim()};Password={SqlPassword};TrustServerCertificate=True;"
            : $"Server={server};Database={database};Integrated Security=True;TrustServerCertificate=True;";
    }
}
