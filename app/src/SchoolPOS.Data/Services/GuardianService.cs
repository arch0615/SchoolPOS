using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Entities;
using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Data.Services;

/// <summary>
/// Implementación del servicio del portal para tutores. El inicio de sesión aplica bloqueo tras
/// <see cref="MaxFailedAttempts"/> intentos fallidos durante <see cref="LockoutMinutes"/> minutos
/// (FR-WP-3). Nunca almacena la contraseña en claro.
/// </summary>
public sealed class GuardianService : IGuardianService
{
    private readonly SchoolDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IClock _clock;

    private const int MaxFailedAttempts = 5;
    private const int LockoutMinutes = 15;

    public GuardianService(SchoolDbContext db, IPasswordHasher hasher, IClock clock)
    {
        _db = db;
        _hasher = hasher;
        _clock = clock;
    }

    public async Task<Guardian> RegisterAsync(
        Guid schoolId, string email, string password, string fullName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("El correo es obligatorio.", nameof(email));
        if (string.IsNullOrEmpty(password) || password.Length < 6)
            throw new ArgumentException("La contraseña debe tener al menos 6 caracteres.", nameof(password));

        var normalized = email.Trim().ToLowerInvariant();
        var exists = await _db.Guardians.AnyAsync(g => g.SchoolId == schoolId && g.Email == normalized, ct);
        if (exists)
            throw new InvalidOperationException("Ya existe una cuenta con ese correo.");

        var guardian = new Guardian
        {
            SchoolId = schoolId,
            Email = normalized,
            FullName = fullName,
            PasswordHash = _hasher.Hash(password),
            CreatedAtUtc = _clock.UtcNow,
        };
        _db.Guardians.Add(guardian);
        await _db.SaveChangesAsync(ct);
        return guardian;
    }

    public async Task<GuardianAuthResult> AuthenticateAsync(
        Guid schoolId, string email, string password, CancellationToken ct = default)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var guardian = await _db.Guardians
            .FirstOrDefaultAsync(g => g.SchoolId == schoolId && g.Email == normalized, ct);

        if (guardian is null)
            return GuardianAuthResult.Fail("Correo o contraseña incorrectos.");

        var now = _clock.UtcNow;
        if (guardian.LockedUntilUtc is { } until && until > now)
            return GuardianAuthResult.Locked($"Cuenta bloqueada temporalmente. Intente después de {until:HH:mm} UTC.");

        if (!_hasher.Verify(password, guardian.PasswordHash))
        {
            guardian.FailedLoginCount++;
            if (guardian.FailedLoginCount >= MaxFailedAttempts)
            {
                guardian.LockedUntilUtc = now.AddMinutes(LockoutMinutes);
                guardian.FailedLoginCount = 0;
                await _db.SaveChangesAsync(ct);
                return GuardianAuthResult.Locked(
                    $"Demasiados intentos. Cuenta bloqueada {LockoutMinutes} minutos.");
            }
            await _db.SaveChangesAsync(ct);
            return GuardianAuthResult.Fail("Correo o contraseña incorrectos.");
        }

        // Éxito: limpia contador y bloqueo.
        if (guardian.FailedLoginCount != 0 || guardian.LockedUntilUtc is not null)
        {
            guardian.FailedLoginCount = 0;
            guardian.LockedUntilUtc = null;
            await _db.SaveChangesAsync(ct);
        }
        return GuardianAuthResult.Ok(guardian);
    }

    public async Task LinkStudentByEnrollmentAsync(
        Guid guardianId, Guid schoolId, string enrollmentNo, CancellationToken ct = default)
    {
        var code = enrollmentNo.Trim();
        var student = await _db.Students
            .FirstOrDefaultAsync(s => s.SchoolId == schoolId && s.EnrollmentNo == code && s.IsActive, ct)
            ?? throw new InvalidOperationException($"No se encontró un estudiante con matrícula '{code}'.");

        var already = await _db.GuardianStudents
            .AnyAsync(gs => gs.GuardianId == guardianId && gs.StudentId == student.Id, ct);
        if (already)
            return;

        _db.GuardianStudents.Add(new GuardianStudent
        {
            GuardianId = guardianId,
            StudentId = student.Id,
            LinkedAtUtc = _clock.UtcNow,
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<LinkedStudent>> GetLinkedStudentsAsync(
        Guid guardianId, CancellationToken ct = default)
    {
        // Joins explícitos (portables entre proveedores; SQLite no traduce la proyección
        // multinivel a través de navegaciones de referencia).
        var query =
            from gs in _db.GuardianStudents.AsNoTracking()
            where gs.GuardianId == guardianId
            join s in _db.Students on gs.StudentId equals s.Id
            join a in _db.Accounts on s.Id equals a.StudentId
            orderby s.FullName
            select new LinkedStudent(s.Id, a.Id, s.EnrollmentNo, s.FullName, a.Balance);

        return await query.ToListAsync(ct);
    }

    public Task<bool> OwnsStudentAsync(Guid guardianId, Guid studentId, CancellationToken ct = default) =>
        _db.GuardianStudents.AnyAsync(gs => gs.GuardianId == guardianId && gs.StudentId == studentId, ct);

    public async Task<IReadOnlyList<MovementRow>> GetMovementsAsync(
        Guid accountId, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default)
    {
        var query = _db.BalanceMovements.AsNoTracking().Where(m => m.AccountId == accountId);
        if (fromUtc is { } from)
            query = query.Where(m => m.CreatedAtUtc >= from);
        if (toUtc is { } to)
            query = query.Where(m => m.CreatedAtUtc <= to);

        return await query
            .OrderByDescending(m => m.CreatedAtUtc)
            .Take(200)
            .Select(m => new MovementRow(
                m.CreatedAtUtc, m.Type.ToString(), m.Amount, m.BalanceAfter, m.Reference))
            .ToListAsync(ct);
    }

    public Task<Guardian?> GetAsync(Guid guardianId, CancellationToken ct = default) =>
        _db.Guardians.AsNoTracking().FirstOrDefaultAsync(g => g.Id == guardianId, ct);

    public async Task<string?> RequestPasswordResetAsync(
        Guid schoolId, string email, CancellationToken ct = default)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var guardian = await _db.Guardians
            .FirstOrDefaultAsync(g => g.SchoolId == schoolId && g.Email == normalized, ct);
        if (guardian is null)
            return null; // no revelar si la cuenta existe

        // Token aleatorio (hex, url-safe). Se guarda solo su hash; caduca en 1 hora.
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        guardian.PasswordResetTokenHash = _hasher.Hash(token);
        guardian.PasswordResetExpiresUtc = _clock.UtcNow.AddHours(1);
        await _db.SaveChangesAsync(ct);
        return token;
    }

    public async Task<bool> ResetPasswordAsync(
        Guid schoolId, string email, string token, string newPassword, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 6)
            return false;

        var normalized = email.Trim().ToLowerInvariant();
        var guardian = await _db.Guardians
            .FirstOrDefaultAsync(g => g.SchoolId == schoolId && g.Email == normalized, ct);
        if (guardian?.PasswordResetTokenHash is null || guardian.PasswordResetExpiresUtc is null)
            return false;
        if (guardian.PasswordResetExpiresUtc < _clock.UtcNow)
            return false;
        if (!_hasher.Verify(token, guardian.PasswordResetTokenHash))
            return false;

        guardian.PasswordHash = _hasher.Hash(newPassword);
        guardian.PasswordResetTokenHash = null; // un solo uso
        guardian.PasswordResetExpiresUtc = null;
        guardian.FailedLoginCount = 0;          // limpia bloqueo
        guardian.LockedUntilUtc = null;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> ChangePasswordAsync(
        Guid guardianId, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 6)
            return false;

        var guardian = await _db.Guardians.FirstOrDefaultAsync(g => g.Id == guardianId, ct);
        if (guardian is null || !_hasher.Verify(currentPassword, guardian.PasswordHash))
            return false;

        guardian.PasswordHash = _hasher.Hash(newPassword);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task UpdateProfileAsync(Guid guardianId, string fullName, CancellationToken ct = default)
    {
        var guardian = await _db.Guardians.FirstOrDefaultAsync(g => g.Id == guardianId, ct)
            ?? throw new InvalidOperationException("Tutor no encontrado.");
        guardian.FullName = fullName;
        await _db.SaveChangesAsync(ct);
    }
}
