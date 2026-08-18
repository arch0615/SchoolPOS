using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SchoolPOS.Data;

namespace SchoolPOS.Data.Migrations.Sqlite;

/// <summary>
/// Fábrica en tiempo de diseño de este juego de migraciones. Vive aquí, y no en
/// <c>SchoolPOS.Data</c>, porque <c>dotnet ef</c> busca el contexto en el ensamblado destino: sin
/// una fábrica local no encuentra <see cref="SchoolDbContext"/> aunque esté referenciado.
/// <para>
/// La cadena no se usa para nada real: generar una migración no toca la base.
/// </para>
/// </summary>
public sealed class SqliteDesignTimeFactory : IDesignTimeDbContextFactory<SchoolDbContext>
{
    public SchoolDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SchoolDbContext>()
            .UseSqlite(
                "Data Source=schoolpos-design.db",
                o => o.MigrationsAssembly(SqliteMigrations.AssemblyName))
            .Options;
        return new SchoolDbContext(options);
    }
}
