using Microsoft.EntityFrameworkCore;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Entities;
using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Data.Services;

/// <summary>
/// Autenticación de operadores del POS contra la DB local, con hash de contraseña y rol.
/// Aplica bloqueo tras <see cref="MaxFailedAttempts"/> intentos fallidos durante
/// <see cref="LockoutMinutes"/> minutos, igual que el portal del tutor (NFR-6): la caja del POS
/// está en una LAN, pero la misma cuenta de operador abre también la consola web de la tienda.
/// </summary>
public sealed class AuthService : IAuthService
{
    private readonly SchoolDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IClock _clock;

    private const int MaxFailedAttempts = 5;
    private const int LockoutMinutes = 15;

    public AuthService(SchoolDbContext db, IPasswordHasher hasher, IClock clock)
    {
        _db = db;
        _hasher = hasher;
        _clock = clock;
    }

    public async Task<AuthResult> AuthenticateAsync(
        Guid schoolId, string username, string password, CancellationToken ct = default)
    {
        // Con seguimiento: un intento fallido tiene que poder persistir el contador.
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.SchoolId == schoolId && u.Username == username, ct);

        // Mensaje genérico para no revelar si el usuario existe.
        if (user is null || !user.IsActive)
            return AuthResult.Fail("Usuario o contraseña incorrectos.");

        var now = _clock.UtcNow;
        if (user.LockedUntilUtc is { } until && until > now)
            return AuthResult.Locked($"Cuenta bloqueada temporalmente. Intente después de {until:HH:mm} UTC.");

        if (!_hasher.Verify(password, user.PasswordHash))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailedAttempts)
            {
                user.LockedUntilUtc = now.AddMinutes(LockoutMinutes);
                user.FailedLoginCount = 0;
                await _db.SaveChangesAsync(ct);
                return AuthResult.Locked($"Demasiados intentos. Cuenta bloqueada {LockoutMinutes} minutos.");
            }
            await _db.SaveChangesAsync(ct);
            return AuthResult.Fail("Usuario o contraseña incorrectos.");
        }

        // Éxito: limpia contador y bloqueo.
        if (user.FailedLoginCount != 0 || user.LockedUntilUtc is not null)
        {
            user.FailedLoginCount = 0;
            user.LockedUntilUtc = null;
            await _db.SaveChangesAsync(ct);
        }
        return AuthResult.Ok(user);
    }

    public async Task<User> CreateOperatorAsync(
        Guid schoolId, string username, string password, UserRole role, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("El usuario es obligatorio.", nameof(username));

        var exists = await _db.Users.AnyAsync(u => u.SchoolId == schoolId && u.Username == username, ct);
        if (exists)
            throw new InvalidOperationException($"El usuario '{username}' ya existe en esta escuela.");

        var user = new User
        {
            SchoolId = schoolId,
            Username = username,
            PasswordHash = _hasher.Hash(password),
            Role = role,
            IsActive = true,
            CreatedAtUtc = _clock.UtcNow,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);
        return user;
    }
}
