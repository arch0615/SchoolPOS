using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SchoolPOS.Data;

namespace SchoolPOS.Portal.Web.Tests;

/// <summary>
/// Levanta el Portal real (mismo Program.cs, mismos middlewares, misma tubería de autenticación)
/// contra una base SQLite en memoria propia de cada prueba, en vez de la que use el entorno de
/// desarrollo. La conexión se abre aquí y se mantiene viva mientras la fábrica exista — igual que
/// <c>TestDatabase</c> en SchoolPOS.Data.Tests, para que el esquema no se pierda entre usos.
/// <para>
/// Solo reemplaza el registro de <see cref="SchoolDbContext"/> y apaga la siembra de datos de
/// demostración; todo lo demás (Program.cs, el middleware de autenticación de
/// <c>/api/sync/*</c>, las políticas de autorización) es el código real, no un doble.
/// </para>
/// </summary>
public sealed class SyncApiWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public SyncApiWebApplicationFactory() => _connection.Open();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            // Sin esto la fábrica sembraría los datos de demostración del entorno de desarrollo
            // en cada prueba; cada prueba siembra exactamente lo que necesita.
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Portal:SeedDemoData"] = "false",
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<SchoolDbContext>>();
            // MigrationsAssembly es obligatorio aquí: sin él, EF usa por omisión el ensamblado
            // donde vive SchoolDbContext (SchoolPOS.Data), que trae las migraciones de SQL
            // Server — exactamente el error que el juego separado de migraciones de SQLite
            // existe para evitar, y da un "near 'max': syntax error" al aplicarlas sobre SQLite.
            services.AddDbContext<SchoolDbContext>(options =>
                options.UseSqlite(_connection, o => o.MigrationsAssembly(SqliteMigrations.AssemblyName)));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _connection.Dispose();
    }
}
