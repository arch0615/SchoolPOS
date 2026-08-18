using Microsoft.EntityFrameworkCore;
using SchoolPOS.Data;
using SchoolPOS.Data.Security;
using SchoolPOS.Data.Services;
using SchoolPOS.Domain.Entities;
using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Pos.Desktop.Infrastructure;

/// <summary>
/// Prepara la base de datos de una escuela desde el asistente de primer arranque: crea el esquema,
/// la escuela y su operador administrador. Es la misma operación que hacía únicamente la
/// herramienta de línea de comandos <c>SchoolPOS.Provision</c>, para que instalar ya no dependa de
/// que alguien sepa abrir una terminal.
/// </summary>
public static class PosProvisioner
{
    /// <summary>Crea el esquema y devuelve el contexto listo. Idempotente sobre una base existente.</summary>
    private static async Task<SchoolDbContext> OpenAsync(string provider, string connectionString, CancellationToken ct)
    {
        var builder = new DbContextOptionsBuilder<SchoolDbContext>();
        var isSqlite = string.Equals(provider, PosConfig.SqliteProvider, StringComparison.OrdinalIgnoreCase);
        if (isSqlite)
            builder.UseSqlite(connectionString, o => o.MigrationsAssembly(SqliteMigrations.AssemblyName));
        else
            builder.UseSqlServer(connectionString);

        var db = new SchoolDbContext(builder.Options);

        // Migraciones en ambos proveedores. Antes SQLite usaba EnsureCreated, que arma el esquema
        // una vez y no lo altera nunca: una escuela ya instalada no podía recibir ningún cambio de
        // esquema, y la app fallaba con un error de base de datos al tocar la columna nueva.
        await db.Database.MigrateAsync(ct);

        return db;
    }

    /// <summary>Comprueba que se puede abrir la base (o crearla). Devuelve el error legible si no.</summary>
    public static async Task<string?> TestAsync(string provider, string connectionString, CancellationToken ct = default)
    {
        try
        {
            await using var db = await OpenAsync(provider, connectionString, ct);
            await db.Database.CanConnectAsync(ct);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// Crea (si no existen) la escuela y su administrador, y devuelve el Id de la escuela. Si la
    /// base ya tiene una escuela, la reutiliza en vez de crear una segunda: una base local
    /// corresponde a una escuela.
    /// </summary>
    public static async Task<Guid> ProvisionAsync(
        string provider, string connectionString, string schoolName,
        string adminUser, string adminPassword, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(provider, connectionString, ct);

        var school = await db.Schools.FirstOrDefaultAsync(ct);
        if (school is null)
        {
            school = new School
            {
                Name = schoolName.Trim(),
                Currency = "MXN",
                CommissionRate = 0.05m,
                TaxRate = 0m,
                TaxInclusive = true,
                CreatedAtUtc = DateTime.UtcNow,
            };
            db.Schools.Add(school);
            await db.SaveChangesAsync(ct);
        }

        var user = adminUser.Trim();
        var exists = await db.Users.AnyAsync(u => u.SchoolId == school.Id && u.Username == user, ct);
        if (!exists)
        {
            var auth = new AuthService(db, new Pbkdf2PasswordHasher(), new SystemClock());
            await auth.CreateOperatorAsync(school.Id, user, adminPassword, UserRole.Admin, ct);
        }

        return school.Id;
    }
}
