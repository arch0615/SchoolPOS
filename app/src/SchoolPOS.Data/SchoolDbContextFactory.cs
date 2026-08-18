using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SchoolPOS.Data;

/// <summary>
/// Fábrica en tiempo de diseño de las migraciones de <b>SQL Server</b> (<c>dotnet ef</c>). No se
/// conecta a la base al generarlas; la cadena real se inyecta por escuela en tiempo de ejecución.
/// <para>
/// Las de SQLite viven en <c>SchoolPOS.Data.Migrations.Sqlite</c> y tienen su propia fábrica: el
/// DDL de una migración es específico del proveedor, y EF descubre todas las del ensamblado de
/// migraciones, así que los dos juegos no pueden convivir en uno solo.
/// </para>
/// <example>
/// Tras cambiar el modelo hay que agregar la migración a <b>los dos</b> juegos:
/// <code>
/// dotnet ef migrations add &lt;Nombre&gt; --project src/SchoolPOS.Data -o Migrations
///
/// dotnet ef migrations add &lt;Nombre&gt; \
///     --project src/SchoolPOS.Data.Migrations.Sqlite \
///     --startup-project src/SchoolPOS.Data.Migrations.Sqlite -o Migrations
/// </code>
/// </example>
/// </summary>
public sealed class SchoolDbContextFactory : IDesignTimeDbContextFactory<SchoolDbContext>
{
    public SchoolDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SchoolDbContext>()
            .UseSqlServer("Server=localhost;Database=SchoolPOS;Trusted_Connection=True;TrustServerCertificate=True;")
            .Options;
        return new SchoolDbContext(options);
    }
}
