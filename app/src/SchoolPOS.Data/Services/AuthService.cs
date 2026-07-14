using Microsoft.EntityFrameworkCore;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Entities;
using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Data.Services;

/// <summary>Autenticación de operadores del POS contra la DB local, con hash de contraseña y rol.</summary>
public sealed class AuthService : IAuthService
{
    private readonly SchoolDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IClock _clock;

    public AuthService(SchoolDbContext db, IPasswordHasher hasher, IClock clock)
    {
        _db = db;
        _hasher = hasher;
        _clock = clock;
    }

    public async Task<AuthResult> AuthenticateAsync(
        Guid schoolId, string username, string password, CancellationToken ct = default)
    {
        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.SchoolId == schoolId && u.Username == username, ct);

        // Mensaje genérico para no revelar si el usuario existe.
        if (user is null || !user.IsActive || !_hasher.Verify(password, user.PasswordHash))
            return AuthResult.Fail("Usuario o contraseña incorrectos.");

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
