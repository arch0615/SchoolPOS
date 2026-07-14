using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SchoolPOS.Data;

/// <summary>
/// Fábrica en tiempo de diseño para generar migraciones (<c>dotnet ef</c>). No se conecta a la
/// base al crear migraciones; la cadena real se inyecta por escuela en tiempo de ejecución.
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
