using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SchoolPOS.Data;
using SchoolPOS.Domain.Entities;
using SchoolPOS.Portal.Web.Infrastructure;

namespace SchoolPOS.Portal.Web.Tests;

/// <summary>
/// Una escuela dada de baja (FR-ADM) no debe aparecer como opción de registro/ingreso del tutor,
/// ni pasar la validación de un POST manipulado con su SchoolId todavía.
/// </summary>
public sealed class SchoolDirectoryTests
{
    private static SchoolDbContext NewContext()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var db = new SchoolDbContext(new DbContextOptionsBuilder<SchoolDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task Inactive_school_is_excluded_from_the_list_and_fails_existence_check()
    {
        using var db = NewContext();
        var active = new School { Name = "Activa", Currency = "MXN" };
        var inactive = new School { Name = "De baja", Currency = "MXN", IsActive = false };
        db.Schools.AddRange(active, inactive);
        await db.SaveChangesAsync();

        var directory = new SchoolDirectory(db);

        var listed = await directory.ListAsync();
        listed.Should().ContainSingle(o => o.Id == active.Id);
        listed.Should().NotContain(o => o.Id == inactive.Id);

        (await directory.ExistsAsync(active.Id)).Should().BeTrue();
        (await directory.ExistsAsync(inactive.Id)).Should().BeFalse("no debe poder registrar/ingresar tutores en una escuela dada de baja");
    }
}
