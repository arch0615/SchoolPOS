using Microsoft.Extensions.DependencyInjection;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Pos.Desktop.Infrastructure;

namespace SchoolPOS.Pos.Desktop.ViewModels;

/// <summary>Inicio de sesión del operador (FR-POS-1). Autentica contra la DB local.</summary>
public sealed class LoginViewModel : ViewModelBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PosSession _session;

    private string _username = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public LoginViewModel(IServiceScopeFactory scopeFactory, PosSession session)
    {
        _scopeFactory = scopeFactory;
        _session = session;
        LoginCommand = new AsyncRelayCommand(LoginAsync, () => !IsBusy);
    }

    public string Username
    {
        get => _username;
        set => SetProperty(ref _username, value);
    }

    /// <summary>Contraseña (asignada desde el code-behind por seguridad del PasswordBox).</summary>
    public string Password { private get; set; } = string.Empty;

    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set { if (SetProperty(ref _isBusy, value)) LoginCommand.RaiseCanExecuteChanged(); }
    }

    public AsyncRelayCommand LoginCommand { get; }

    /// <summary>Se dispara al autenticar correctamente (transición a la ventana principal).</summary>
    public event Action? LoginSucceeded;

    private async Task LoginAsync()
    {
        ErrorMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrEmpty(Password))
        {
            ErrorMessage = "Ingrese usuario y contraseña.";
            return;
        }

        IsBusy = true;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
            var result = await auth.AuthenticateAsync(_session.SchoolId, Username, Password);

            if (!result.Succeeded || result.User is null)
            {
                ErrorMessage = result.Error ?? "No se pudo iniciar sesión.";
                return;
            }

            _session.SignIn(result.User);
            LoginSucceeded?.Invoke();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error de conexión con la base local: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
