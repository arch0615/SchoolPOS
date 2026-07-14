using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SchoolPOS.Data;
using SchoolPOS.Data.Security;
using SchoolPOS.Data.Services;
using SchoolPOS.Domain.Entities;
using SchoolPOS.Domain.Enums;

// ---------------------------------------------------------------------------
// SchoolPOS.Provision — provisiona una escuela: aplica migraciones, crea la
// escuela y el operador administrador (contraseña hasheada). Idempotente:
// re-ejecutar con el mismo --SchoolId no duplica nada.
//
// Uso:
//   dotnet run --project tools/SchoolPOS.Provision -- \
//     --ConnectionString "Server=localhost\SQLEXPRESS;Database=SchoolPOS_MiEscuela;Trusted_Connection=True;TrustServerCertificate=True;" \
//     --SchoolName "Colegio X" --AdminUser admin --AdminPassword "Secreta123" \
//     [--Provider SqlServer|Sqlite] [--SchoolId <guid>] [--Currency MXN] \
//     [--CommissionRate 0.05] [--TaxRate 0] [--TaxInclusive true]
// ---------------------------------------------------------------------------

var opts = ArgParser.Parse(args);

string Require(string key) =>
    opts.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v)
        ? v
        : throw new ArgumentException($"Falta el argumento obligatorio --{key}");

string Optional(string key, string fallback) =>
    opts.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;

decimal Dec(string key, string fallback) =>
    decimal.Parse(Optional(key, fallback), CultureInfo.InvariantCulture);

try
{
    var provider = Optional("Provider", "SqlServer");
    var connectionString = Require("ConnectionString");
    var schoolName = Require("SchoolName");
    var adminUser = Optional("AdminUser", "admin");
    var adminPassword = Require("AdminPassword");

    var currency = Optional("Currency", "MXN").ToUpperInvariant();
    var commissionRate = Dec("CommissionRate", "0.05");
    var taxRate = Dec("TaxRate", "0");
    var taxInclusive = bool.Parse(Optional("TaxInclusive", "true"));
    var schoolId = opts.TryGetValue("SchoolId", out var sid) && Guid.TryParse(sid, out var g)
        ? g : Guid.NewGuid();

    var builder = new DbContextOptionsBuilder<SchoolDbContext>();
    var isSqlite = string.Equals(provider, "Sqlite", StringComparison.OrdinalIgnoreCase);
    if (isSqlite) builder.UseSqlite(connectionString);
    else builder.UseSqlServer(connectionString);

    await using var db = new SchoolDbContext(builder.Options);

    Console.WriteLine($"» Proveedor: {provider}");
    Console.WriteLine("» Preparando base de datos…");
    if (isSqlite) await db.Database.EnsureCreatedAsync();
    else await db.Database.MigrateAsync(); // crea/actualiza el esquema con las migraciones EF

    // Escuela (idempotente por Id).
    var school = await db.Schools.FirstOrDefaultAsync(s => s.Id == schoolId);
    if (school is null)
    {
        school = new School
        {
            Id = schoolId,
            Name = schoolName,
            Currency = currency,
            CommissionRate = commissionRate,
            TaxRate = taxRate,
            TaxInclusive = taxInclusive,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Schools.Add(school);
        await db.SaveChangesAsync();
        Console.WriteLine($"✔ Escuela creada: {schoolName}");
    }
    else
    {
        Console.WriteLine($"• Escuela ya existente: {school.Name} (sin cambios)");
    }

    // Operador administrador (idempotente por usuario).
    var auth = new AuthService(db, new Pbkdf2PasswordHasher(), new SystemClock());
    if (!await db.Users.AnyAsync(u => u.SchoolId == school.Id && u.Username == adminUser))
    {
        await auth.CreateOperatorAsync(school.Id, adminUser, adminPassword, UserRole.Admin);
        Console.WriteLine($"✔ Operador administrador creado: {adminUser}");
    }
    else
    {
        Console.WriteLine($"• Operador '{adminUser}' ya existe (sin cambios)");
    }

    Console.WriteLine();
    Console.WriteLine("════════════════════════════════════════════════════════");
    Console.WriteLine(" Provisión completada");
    Console.WriteLine("════════════════════════════════════════════════════════");
    Console.WriteLine($" SchoolId : {school.Id}");
    Console.WriteLine($" Moneda   : {school.Currency}   Comisión: {school.CommissionRate:P2}   IVA: {school.TaxRate:P2}");
    Console.WriteLine();
    Console.WriteLine(" Configura este SchoolId en:");
    Console.WriteLine("   • POS (appsettings.json): Pos:SchoolId");
    Console.WriteLine("   • Portal (appsettings.json): Portal:SchoolId");
    Console.WriteLine("   • Sync Agent: ConnectionStrings:Local apunta a esta DB");
    Console.WriteLine("════════════════════════════════════════════════════════");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"✖ Error de provisión: {ex.Message}");
    return 1;
}

/// <summary>Parser mínimo de argumentos: soporta --clave valor y --clave=valor.</summary>
static class ArgParser
{
    public static Dictionary<string, string> Parse(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (!a.StartsWith("--", StringComparison.Ordinal))
                continue;
            var key = a[2..];
            var eq = key.IndexOf('=');
            if (eq >= 0)
            {
                result[key[..eq]] = key[(eq + 1)..];
            }
            else if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                result[key] = args[++i];
            }
            else
            {
                result[key] = "true"; // bandera sin valor
            }
        }
        return result;
    }
}
