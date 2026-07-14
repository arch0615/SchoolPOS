using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SchoolPOS.Data;
using SchoolPOS.Domain.Entities;

namespace SchoolPOS.Data.Tests.TestSupport;

/// <summary>
/// Base de datos SQLite en memoria como sustituto portátil de SQL Server para probar el libro
/// mayor (transacciones + UPDATE condicional). La conexión se mantiene abierta para conservar
/// el esquema durante toda la prueba. La prueba de concurrencia real contra SQL Server queda
/// para la Fase 5.9.
/// </summary>
public sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    public SchoolDbContext Context { get; }

    public TestDatabase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        Context = NewContext();
        Context.Database.EnsureCreated();
    }

    public SchoolDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<SchoolDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new SchoolDbContext(options);
    }

    /// <summary>Siembra escuela + estudiante + cuenta y devuelve la cuenta creada.</summary>
    public Account SeedAccount(decimal initialBalance = 0m, decimal overdraftLimit = 0m, decimal commissionRate = 0.05m)
    {
        var school = new School { Name = "Colegio Prueba", Currency = "MXN", CommissionRate = commissionRate };
        var student = new Student { SchoolId = school.Id, EnrollmentNo = "A-001", FullName = "Alumno Prueba" };
        var account = new Account { StudentId = student.Id, Balance = initialBalance, OverdraftLimit = overdraftLimit };
        student.Account = account;

        Context.Schools.Add(school);
        Context.Students.Add(student);
        Context.Accounts.Add(account);
        Context.SaveChanges();
        Context.ChangeTracker.Clear();
        return account;
    }

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}
