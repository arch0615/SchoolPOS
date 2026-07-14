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

    /// <summary>Siembra una escuela con su configuración fiscal/comisión.</summary>
    public School SeedSchool(decimal taxRate = 0m, bool taxInclusive = true, decimal commissionRate = 0.05m)
    {
        var school = new School
        {
            Name = "Colegio Prueba",
            Currency = "MXN",
            CommissionRate = commissionRate,
            TaxRate = taxRate,
            TaxInclusive = taxInclusive,
        };
        Context.Schools.Add(school);
        Context.SaveChanges();
        Context.ChangeTracker.Clear();
        return school;
    }

    /// <summary>Siembra estudiante + cuenta 1:1 en la escuela indicada.</summary>
    public Account SeedStudentAccount(
        Guid schoolId, decimal balance = 0m, decimal overdraftLimit = 0m, string enrollmentNo = "A-001")
    {
        var student = new Student { SchoolId = schoolId, EnrollmentNo = enrollmentNo, FullName = "Alumno Prueba" };
        var account = new Account { StudentId = student.Id, Balance = balance, OverdraftLimit = overdraftLimit };
        student.Account = account;
        Context.Students.Add(student);
        Context.Accounts.Add(account);
        Context.SaveChanges();
        Context.ChangeTracker.Clear();
        return account;
    }

    /// <summary>Siembra un producto con existencias iniciales.</summary>
    public Product SeedProduct(
        Guid schoolId, decimal price = 10m, decimal cost = 6m, decimal stock = 0m, decimal minStock = 0m,
        string name = "Producto")
    {
        var product = new Product
        {
            SchoolId = schoolId,
            Name = name,
            Price = price,
            Cost = cost,
            StockOnHand = stock,
            MinStock = minStock,
        };
        Context.Products.Add(product);
        Context.SaveChanges();
        Context.ChangeTracker.Clear();
        return product;
    }

    /// <summary>Atajo: escuela + estudiante + cuenta y devuelve la cuenta creada.</summary>
    public Account SeedAccount(decimal initialBalance = 0m, decimal overdraftLimit = 0m, decimal commissionRate = 0.05m)
    {
        var school = SeedSchool(commissionRate: commissionRate);
        return SeedStudentAccount(school.Id, initialBalance, overdraftLimit);
    }

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}
