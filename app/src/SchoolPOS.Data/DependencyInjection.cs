using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Data.Security;
using SchoolPOS.Data.Services;

namespace SchoolPOS.Data;

/// <summary>Registro de servicios de datos para hosts (POS, Portal, Sync).</summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registra el <see cref="SchoolDbContext"/> sobre SQL Server (DB local por escuela) y todos
    /// los servicios de dominio.
    /// </summary>
    public static IServiceCollection AddSchoolPosData(this IServiceCollection services, string connectionString) =>
        services.AddSchoolPosData(options => options.UseSqlServer(connectionString));

    /// <summary>
    /// Registra el <see cref="SchoolDbContext"/> con un proveedor configurable (p. ej. SQLite para
    /// desarrollo/pruebas locales) y todos los servicios de dominio.
    /// </summary>
    public static IServiceCollection AddSchoolPosData(
        this IServiceCollection services, Action<DbContextOptionsBuilder> configureDb)
    {
        services.AddDbContext<SchoolDbContext>(configureDb);
        return services.AddSchoolPosServices();
    }

    /// <summary>Registra los servicios de dominio (sin el DbContext).</summary>
    public static IServiceCollection AddSchoolPosServices(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IStudentDirectory, StudentDirectory>();
        services.AddScoped<IBalanceService, BalanceService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<ISalesService, SalesService>();
        services.AddScoped<IPurchasingService, PurchasingService>();
        services.AddScoped<ITreasuryService, TreasuryService>();
        services.AddScoped<IGuardianService, GuardianService>();
        services.AddScoped<ICommissionReportService, CommissionReportService>();
        services.AddScoped<ISalesReportService, SalesReportService>();
        services.AddScoped<IFinancialReportService, FinancialReportService>();
        services.AddScoped<IPurchasingReportService, PurchasingReportService>();
        services.AddScoped<IAuditLogQueryService, AuditLogQueryService>();
        // El flujo de recargas requiere que el host (portal) registre un IPaymentGateway
        // (implementación real de Mercado Pago con sus credenciales).
        services.AddScoped<ITopUpService, TopUpService>();
        return services;
    }
}
