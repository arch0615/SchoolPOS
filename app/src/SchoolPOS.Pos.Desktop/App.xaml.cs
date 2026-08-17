using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SchoolPOS.Data;
using SchoolPOS.Pos.Desktop.Infrastructure;
using SchoolPOS.Pos.Desktop.ViewModels;
using SchoolPOS.Pos.Desktop.Views;

namespace SchoolPOS.Pos.Desktop;

/// <summary>
/// Punto de entrada del POS. Configura el contenedor de servicios (DI), la DB local por escuela
/// y muestra la ventana de inicio de sesión. El POS opera contra la DB local por LAN, por lo que
/// sigue vendiendo aunque no haya internet (NFR-2).
/// </summary>
public partial class App : Application
{
    private IHost _host = null!;
    private ILogger<App>? _logger;

    /// <summary>Proveedor de servicios para resolver ventanas en transiciones de código subyacente.</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Red de seguridad global: sin esto, cualquier excepción no atrapada en un manejador de
        // comando (async void) tumba el POS entero a mitad de una venta. Se registra antes de
        // construir el host para cubrir también una falla de arranque (DI, configuración).
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            _host = Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration((_, config) =>
                {
                    config.SetBasePath(AppContext.BaseDirectory);
                    config.AddJsonFile("appsettings.json", optional: false);
                })
                .ConfigureServices((context, services) =>
                {
                    var connectionString = context.Configuration.GetConnectionString("Local")
                        ?? throw new InvalidOperationException("Falta ConnectionStrings:Local en appsettings.json.");
                    services.AddSchoolPosData(connectionString);

                    // Sesión con la escuela de la configuración local.
                    var schoolId = context.Configuration.GetValue<Guid>("Pos:SchoolId");
                    services.AddSingleton(new PosSession { SchoolId = schoolId });

                    // View-models.
                    services.AddTransient<LoginViewModel>();
                    services.AddTransient<MainViewModel>();
                    services.AddTransient<DashboardViewModel>();
                    services.AddTransient<SalesViewModel>();
                    services.AddTransient<RefundsViewModel>();
                    services.AddTransient<InventoryViewModel>();
                    services.AddTransient<PurchasingViewModel>();
                    services.AddTransient<TreasuryViewModel>();
                    services.AddTransient<ReportsViewModel>();
                    services.AddTransient<AuditViewModel>();
                    services.AddTransient<SettingsViewModel>();

                    // Ventanas.
                    services.AddTransient<LoginWindow>();
                    services.AddTransient<MainWindow>();
                })
                .Build();

            Services = _host.Services;
            _logger = Services.GetRequiredService<ILogger<App>>();

            var login = Services.GetRequiredService<LoginWindow>();
            login.Show();
        }
        catch (Exception ex)
        {
            _logger?.LogCritical(ex, "No se pudo iniciar la aplicación.");
            MessageBox.Show(
                $"No se pudo iniciar el POS. Revisa la configuración (appsettings.json) y la conexión " +
                $"a la base de datos local.\n\nDetalle: {ex.Message}",
                "Error al iniciar", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    /// <summary>
    /// Excepción no atrapada en el hilo de UI (p. ej. dentro de un comando async void). Se
    /// registra y se muestra un aviso; la app sigue viva — marcar Handled evita el cierre.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "Excepción no atrapada en el hilo de UI.");
        MessageBox.Show(
            $"Ocurrió un error inesperado. Puedes seguir usando el POS, pero si el problema persiste, " +
            $"reinicia la aplicación.\n\nDetalle: {e.Exception.Message}",
            "Error inesperado", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }

    /// <summary>
    /// Excepción no atrapada fuera del hilo de UI. .NET va a terminar el proceso de todos modos
    /// (no se puede evitar aquí) — esto solo garantiza que quede registrada antes de morir.
    /// </summary>
    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        _logger?.LogCritical(ex, "Excepción no atrapada fuera del hilo de UI. La aplicación se cerrará.");
        MessageBox.Show(
            "Ocurrió un error crítico y la aplicación se cerrará.\n\n" +
            $"Detalle: {ex?.Message ?? "desconocido"}",
            "Error crítico", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    /// <summary>Tarea en segundo plano cuya excepción nunca se observó (nadie hizo await). Solo registro.</summary>
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "Excepción no observada en una tarea en segundo plano.");
        e.SetObserved();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        base.OnExit(e);
    }
}
