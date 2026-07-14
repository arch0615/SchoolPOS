using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SchoolPOS.Data;
using SchoolPOS.Domain.Entities;

namespace SchoolPOS.Data.Tests.TestSupport;

/// <summary>
/// Base de datos SQLite <b>en archivo</b> con WAL + <c>busy_timeout</c>, para pruebas de
/// concurrencia con múltiples conexiones/contextos simultáneos (cada tarea usa el suyo). SQLite
/// serializa los escritores, lo que —junto con el UPDATE condicional del ledger— evita sobregiro y
/// actualizaciones perdidas. La prueba adversarial sobre SQL Server real está aparte (gated).
/// </summary>
public sealed class SqliteConcurrencyDb : IDisposable
{
    private readonly string _file;

    public SqliteConcurrencyDb()
    {
        _file = Path.Combine(Path.GetTempPath(), $"schoolpos-conc-{Guid.NewGuid():N}.db");
        var (conn, ctx) = Create();
        try { ctx.Database.EnsureCreated(); }
        finally { ctx.Dispose(); conn.Dispose(); }
    }

    private (SqliteConnection Conn, SchoolDbContext Ctx) Create()
    {
        var conn = new SqliteConnection($"Data Source={_file}");
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=20000;";
            cmd.ExecuteNonQuery();
        }
        var options = new DbContextOptionsBuilder<SchoolDbContext>().UseSqlite(conn).Options;
        return (conn, new SchoolDbContext(options));
    }

    /// <summary>Ejecuta una acción con un contexto y conexión propios (aislados por tarea).</summary>
    public async Task<T> WithContextAsync<T>(Func<SchoolDbContext, Task<T>> action)
    {
        var (conn, ctx) = Create();
        try { return await action(ctx); }
        finally { await ctx.DisposeAsync(); conn.Dispose(); }
    }

    /// <summary>Siembra escuela + estudiante + cuenta y devuelve el Id de la cuenta.</summary>
    public Task<Guid> SeedAccountAsync(decimal balance, decimal overdraft = 0m) =>
        WithContextAsync(async ctx =>
        {
            var schoolId = Guid.NewGuid();
            var student = new Student { SchoolId = schoolId, EnrollmentNo = "A-001", FullName = "Alumno" };
            var account = new Account { StudentId = student.Id, Balance = balance, OverdraftLimit = overdraft };
            student.Account = account;
            ctx.Schools.Add(new School { Id = schoolId, Name = "Conc", Currency = "MXN" });
            ctx.Students.Add(student);
            ctx.Accounts.Add(account);
            await ctx.SaveChangesAsync();
            return account.Id;
        });

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
            try { File.Delete(_file + suffix); } catch { /* best effort */ }
    }
}
