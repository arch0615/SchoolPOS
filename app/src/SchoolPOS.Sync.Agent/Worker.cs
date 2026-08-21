using Microsoft.EntityFrameworkCore;
using SchoolPOS.Data;
using SchoolPOS.Data.Services;
using SchoolPOS.Data.Sync;
using SchoolPOS.Domain.Abstractions;

namespace SchoolPOS.Sync.Agent;

/// <summary>
/// Servicio en segundo plano que ejecuta el <see cref="SyncAgent"/> en un intervalo. Corre en cada
/// escuela: baja recargas confirmadas de la nube al ledger local y sube el consumo. La nube se ve
/// por <c>/api/sync/*</c> (<see cref="HttpSyncApiClient"/>) — este proceso solo tiene la llave de
/// esta escuela, nunca una cadena de conexión a la base de datos de la nube (FR-SYNC-API).
/// Tolerante a fallas: si una corrida falla (p. ej. sin internet), se registra y se reintenta en
/// la siguiente.
/// </summary>
public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IConfiguration _config;
    private readonly IClock _clock;
    private readonly HttpSyncApiClient _cloud;

    public Worker(ILogger<Worker> logger, IConfiguration config, IClock clock, HttpSyncApiClient cloud)
    {
        _logger = logger;
        _config = config;
        _clock = clock;
        _cloud = cloud;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, _config.GetValue("Sync:IntervalSeconds", 30)));
        _logger.LogInformation("Agente de sincronización iniciado (intervalo {Interval}s).", interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var local = CreateLocalContext();
                var agent = new SyncAgent(_cloud, local, new BalanceService(local, _clock), _clock);

                var report = await agent.RunOnceAsync(stoppingToken);
                if (report.TopUpsPulled > 0 || report.MovementsPushed > 0 || report.RosterPushed > 0
                    || report.HasFailures || report.HasPendingRoster)
                    _logger.LogInformation(
                        "Sync: {Applied}/{Pulled} recargas aplicadas, {Failed} fallidas, " +
                        "{RosterPushed} alumnos subidos/actualizados, {Pushed} movimientos subidos, " +
                        "{Skipped} en espera del roster de la nube.",
                        report.TopUpsApplied, report.TopUpsPulled, report.TopUpsFailed,
                        report.RosterPushed, report.MovementsPushed, report.MovementsSkipped);
            }
            catch (Exception ex)
            {
                // Falla de conexión (nube o local): se reintenta en el próximo ciclo.
                _logger.LogWarning(ex, "Corrida de sincronización fallida; se reintentará.");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    private SchoolDbContext CreateLocalContext()
    {
        var provider = _config["Database:Provider"] ?? "Sqlite";
        var connectionString = _config.GetConnectionString("Local")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:Local.");

        var options = new DbContextOptionsBuilder<SchoolDbContext>();
        if (string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
            options.UseSqlServer(connectionString);
        else
            options.UseSqlite(connectionString);
        return new SchoolDbContext(options.Options);
    }
}
