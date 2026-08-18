using Microsoft.EntityFrameworkCore;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Data.Services;

/// <summary>Implementación del mantenimiento de operadores del POS.</summary>
public sealed class OperatorRegistry : IOperatorRegistry
{
    private readonly SchoolDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IClock _clock;

    public OperatorRegistry(SchoolDbContext db, IPasswordHasher hasher, IClock clock)
    {
        _db = db;
        _hasher = hasher;
        _clock = clock;
    }

    public async Task<IReadOnlyList<OperatorRow>> ListAsync(
        Guid schoolId, bool includeInactive = false, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        return await _db.Users.AsNoTracking()
            .Where(u => u.SchoolId == schoolId && (includeInactive || u.IsActive))
            .OrderBy(u => u.Username)
            .Select(u => new OperatorRow(
                u.Id, u.Username, u.Role, u.IsActive,
                u.LockedUntilUtc != null && u.LockedUntilUtc > now,
                u.CreatedAtUtc))
            .ToListAsync(ct);
    }

    public async Task SetRoleAsync(Guid userId, UserRole role, CancellationToken ct = default)
    {
        var user = await FindAsync(userId, ct);

        // No dejar a la escuela sin ningún administrador: nadie podría volver a entrar a
        // configuración, reportes ni a esta misma pantalla.
        if (user.Role == UserRole.Admin && role != UserRole.Admin)
            await EnsureAnotherAdminExistsAsync(user, ct);

        user.Role = role;
        await _db.SaveChangesAsync(ct);
    }

    public async Task SetActiveAsync(Guid userId, bool isActive, CancellationToken ct = default)
    {
        var user = await FindAsync(userId, ct);
        if (!isActive && user.Role == UserRole.Admin)
            await EnsureAnotherAdminExistsAsync(user, ct);

        user.IsActive = isActive;
        if (isActive)
        {
            // Reactivar limpia el bloqueo: si no, volvería con el contador heredado.
            user.FailedLoginCount = 0;
            user.LockedUntilUtc = null;
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task ResetPasswordAsync(Guid userId, string newPassword, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 6)
            throw new ArgumentException("La contraseña debe tener al menos 6 caracteres.", nameof(newPassword));

        var user = await FindAsync(userId, ct);
        user.PasswordHash = _hasher.Hash(newPassword);
        user.FailedLoginCount = 0;   // una contraseña nueva no debe llegar bloqueada
        user.LockedUntilUtc = null;
        await _db.SaveChangesAsync(ct);
    }

    public async Task UnlockAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await FindAsync(userId, ct);
        user.FailedLoginCount = 0;
        user.LockedUntilUtc = null;
        await _db.SaveChangesAsync(ct);
    }

    private async Task<Domain.Entities.User> FindAsync(Guid userId, CancellationToken ct) =>
        await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
        ?? throw new InvalidOperationException("Operador no encontrado.");

    private async Task EnsureAnotherAdminExistsAsync(Domain.Entities.User user, CancellationToken ct)
    {
        var others = await _db.Users.CountAsync(
            u => u.SchoolId == user.SchoolId && u.Id != user.Id &&
                 u.Role == UserRole.Admin && u.IsActive, ct);
        if (others == 0)
            throw new InvalidOperationException(
                "Es el único administrador activo de la escuela. Nombre a otro antes de cambiarlo.");
    }
}
