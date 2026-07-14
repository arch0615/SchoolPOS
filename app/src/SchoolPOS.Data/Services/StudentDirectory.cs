using Microsoft.EntityFrameworkCore;
using SchoolPOS.Domain.Abstractions;

namespace SchoolPOS.Data.Services;

/// <summary>Implementación de la identificación de estudiantes por matrícula o credencial.</summary>
public sealed class StudentDirectory : IStudentDirectory
{
    private readonly SchoolDbContext _db;

    public StudentDirectory(SchoolDbContext db) => _db = db;

    public async Task<StudentBalance?> FindByCodeAsync(Guid schoolId, string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;

        return await _db.Students.AsNoTracking()
            .Where(s => s.SchoolId == schoolId && s.IsActive &&
                        (s.EnrollmentNo == code || s.CardCode == code))
            .Select(s => new StudentBalance(
                s.Id, s.Account.Id, s.EnrollmentNo, s.FullName, s.Account.Balance))
            .FirstOrDefaultAsync(ct);
    }
}
