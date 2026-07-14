using Microsoft.EntityFrameworkCore;
using SchoolPOS.Data;
using SchoolPOS.Domain.Entities;

namespace SchoolPOS.Portal.Web.Infrastructure;

/// <summary>
/// Siembra datos de demostración (solo desarrollo): una escuela con la configuración esperada y un
/// par de estudiantes con cuenta, para poder registrar un tutor y vincular por matrícula.
/// </summary>
public static class DemoDataSeeder
{
    public static async Task SeedAsync(SchoolDbContext db, Guid schoolId, CancellationToken ct = default)
    {
        if (await db.Schools.AnyAsync(s => s.Id == schoolId, ct))
            return;

        var school = new School
        {
            Id = schoolId,
            Name = "Escuela Demo",
            Currency = "MXN",
            CommissionRate = 0.05m,
            TaxRate = 0m,
            TaxInclusive = true,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Schools.Add(school);

        AddStudent(db, schoolId, "MAT-001", "Ana López");
        AddStudent(db, schoolId, "MAT-002", "Luis Pérez");

        await db.SaveChangesAsync(ct);
    }

    private static void AddStudent(SchoolDbContext db, Guid schoolId, string enrollmentNo, string name)
    {
        var student = new Student { SchoolId = schoolId, EnrollmentNo = enrollmentNo, FullName = name };
        var account = new Account { StudentId = student.Id, Balance = 0m };
        student.Account = account;
        db.Students.Add(student);
        db.Accounts.Add(account);
    }
}
