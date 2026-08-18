using Microsoft.EntityFrameworkCore;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Entities;

namespace SchoolPOS.Data.Services;

/// <summary>
/// Implementación del padrón de alumnos. El alta crea al alumno y su cuenta de saldo en una sola
/// transacción: un alumno sin cuenta no podría comprar, así que no debe existir ese estado
/// intermedio ni aunque falle a la mitad.
/// </summary>
public sealed class StudentRegistry : IStudentRegistry
{
    private readonly SchoolDbContext _db;
    private readonly IClock _clock;

    public StudentRegistry(SchoolDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<IReadOnlyList<StudentRow>> ListAsync(
        Guid schoolId, string? search = null, bool includeInactive = false, CancellationToken ct = default)
    {
        // El filtro va sobre la entidad, no sobre la proyección: EF no traduce un Where/OrderBy
        // aplicado a los miembros de un record ya proyectado.
        var students = _db.Students.AsNoTracking().Where(s => s.SchoolId == schoolId);
        if (!includeInactive)
            students = students.Where(s => s.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            // En minúsculas por el mismo motivo que la búsqueda de productos: SQLite distingue
            // mayúsculas y SQL Server no, y el padrón se busca tecleando a mano.
            var needle = search.Trim().ToLower();
            students = students.Where(s =>
                s.FullName.ToLower().Contains(needle) ||
                s.EnrollmentNo.ToLower().Contains(needle) ||
                (s.CardCode != null && s.CardCode.ToLower().Contains(needle)));
        }

        return await (
            from s in students
            join a in _db.Accounts.AsNoTracking() on s.Id equals a.StudentId
            orderby s.FullName
            select new StudentRow(
                s.Id, a.Id, s.EnrollmentNo, s.CardCode, s.FullName, a.Balance, s.IsActive))
            .Take(500)
            .ToListAsync(ct);
    }

    public Task<Student> CreateAsync(
        Guid schoolId, string enrollmentNo, string fullName, string? cardCode,
        CancellationToken ct = default)
    {
        var enrollment = (enrollmentNo ?? string.Empty).Trim();
        var name = (fullName ?? string.Empty).Trim();
        var card = string.IsNullOrWhiteSpace(cardCode) ? null : cardCode.Trim();

        if (enrollment.Length == 0)
            throw new ArgumentException("La matrícula es obligatoria.", nameof(enrollmentNo));
        if (name.Length == 0)
            throw new ArgumentException("El nombre es obligatorio.", nameof(fullName));

        return _db.ExecuteAtomicAsync(async () =>
        {
            // Se valida antes de insertar para dar un mensaje entendible en vez de dejar que
            // reviente el índice único con un error de base de datos.
            if (await _db.Students.AnyAsync(s => s.SchoolId == schoolId && s.EnrollmentNo == enrollment, ct))
                throw new InvalidOperationException($"Ya existe un alumno con la matrícula '{enrollment}'.");
            if (card is not null &&
                await _db.Students.AnyAsync(s => s.SchoolId == schoolId && s.CardCode == card, ct))
                throw new InvalidOperationException($"Ya existe un alumno con la credencial '{card}'.");

            var student = new Student
            {
                SchoolId = schoolId,
                EnrollmentNo = enrollment,
                FullName = name,
                CardCode = card,
                IsActive = true,
                CreatedAtUtc = _clock.UtcNow,
            };
            var account = new Account
            {
                StudentId = student.Id,
                Balance = 0m,
                OverdraftLimit = 0m,
                UpdatedAtUtc = _clock.UtcNow,
            };
            student.Account = account;

            _db.Students.Add(student);
            _db.Accounts.Add(account);
            await _db.SaveChangesAsync(ct);
            return student;
        }, ct);
    }

    public async Task UpdateAsync(
        Guid studentId, string enrollmentNo, string fullName, string? cardCode,
        CancellationToken ct = default)
    {
        var enrollment = (enrollmentNo ?? string.Empty).Trim();
        var name = (fullName ?? string.Empty).Trim();
        var card = string.IsNullOrWhiteSpace(cardCode) ? null : cardCode.Trim();

        if (enrollment.Length == 0)
            throw new ArgumentException("La matrícula es obligatoria.", nameof(enrollmentNo));
        if (name.Length == 0)
            throw new ArgumentException("El nombre es obligatorio.", nameof(fullName));

        var student = await _db.Students.FirstOrDefaultAsync(s => s.Id == studentId, ct)
            ?? throw new InvalidOperationException("Alumno no encontrado.");

        if (await _db.Students.AnyAsync(
                s => s.SchoolId == student.SchoolId && s.Id != studentId && s.EnrollmentNo == enrollment, ct))
            throw new InvalidOperationException($"Ya existe un alumno con la matrícula '{enrollment}'.");
        if (card is not null && await _db.Students.AnyAsync(
                s => s.SchoolId == student.SchoolId && s.Id != studentId && s.CardCode == card, ct))
            throw new InvalidOperationException($"Ya existe un alumno con la credencial '{card}'.");

        student.EnrollmentNo = enrollment;
        student.FullName = name;
        student.CardCode = card;
        await _db.SaveChangesAsync(ct);
    }

    public async Task SetActiveAsync(Guid studentId, bool isActive, CancellationToken ct = default)
    {
        var student = await _db.Students.FirstOrDefaultAsync(s => s.Id == studentId, ct)
            ?? throw new InvalidOperationException("Alumno no encontrado.");
        student.IsActive = isActive;
        await _db.SaveChangesAsync(ct);
    }
}
