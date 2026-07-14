using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Data.Services;

namespace SchoolPOS.Data;

/// <summary>Registro de servicios de datos para hosts (POS, Portal, Sync).</summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registra el <see cref="SchoolDbContext"/> sobre SQL Server (DB local por escuela),
    /// el reloj de sistema y el servicio de saldo.
    /// </summary>
    public static IServiceCollection AddSchoolPosData(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<SchoolDbContext>(options =>
            options.UseSqlServer(connectionString));

        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<IBalanceService, BalanceService>();
        return services;
    }
}
