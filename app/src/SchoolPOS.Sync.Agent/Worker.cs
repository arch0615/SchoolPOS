using Microsoft.EntityFrameworkCore;
using SchoolPOS.Data;
using SchoolPOS.Data.Services;
using SchoolPOS.Data.Sync;
using SchoolPOS.Domain.Abstractions;

namespace SchoolPOS.Sync.Agent;

/// <summary>
/// Servicio en segundo plano que ejecuta el <see cref="SyncAgent"/> en un intervalo. Corre en cada
/// escuela: baja recargas confirmadas de la nube al ledger local y sube el consumo. Tolerante a
/// fallas: si una corrida falla (p. ej. sin internet), se registra y se reintenta en la siguiente.
/// </summary>
public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IConfiguration _config;
    private readonly IClock _clock;

    public Worker(ILogger<Worker> logger, IConfiguration config, IClock clock)
    {
        _logger = logger;
        _config = config;
        _clock = clock;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, _config.GetValue("Sync:IntervalSeconds", 30)));
        _logger.LogInformation("Agente de sincronización iniciado (intervalo {Interval}s).", interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var cloud = CreateContext("Cloud");
                await using var local = CreateContext("Local");
                var agent = new SyncAgent(cloud, local, new BalanceService(local, _clock), _clock);

                var report = await agent.RunOnceAsync(stoppingToken);
                if (report.TopUpsPulled > 0 || report.MovementsPushed > 0 || report.HasFailures)
                    _logger.LogInformation(
                        "Sync: {Applied}/{Pulled} recargas aplicadas, {Failed} fallidas, {Pushed} movimientos subidos.",
                        report.TopUpsApplied, report.TopUpsPulled, report.TopUpsFailed, report.MovementsPushed);
            }
            catch (Exception ex)
            {
                // Falla de conexión (nube o local): se reintenta en el próximo ciclo.
                _logger.LogWarning(ex, "Corrida de sincronización fallida; se reintentará.");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    private SchoolDbContext CreateContext(string name)
    {
        var provider = _config["Database:Provider"] ?? "Sqlite";
        var connectionString = _config.GetConnectionString(name)
            ?? throw new InvalidOperationException($"Falta ConnectionStrings:{name}.");

        var options = new DbContextOptionsBuilder<SchoolDbContext>();
        if (string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
            options.UseSqlServer(connectionString);
        else
            options.UseSqlite(connectionString);
        return new SchoolDbContext(options.Options);
    }
}
