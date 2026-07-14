using System.IO;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

    /// <summary>Proveedor de servicios para resolver ventanas en transiciones de código subyacente.</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
                services.AddTransient<InventoryViewModel>();
                services.AddTransient<ReportsViewModel>();
                services.AddTransient<AuditViewModel>();

                // Ventanas.
                services.AddTransient<LoginWindow>();
                services.AddTransient<MainWindow>();
            })
            .Build();

        Services = _host.Services;

        var login = Services.GetRequiredService<LoginWindow>();
        login.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        base.OnExit(e);
    }
}
