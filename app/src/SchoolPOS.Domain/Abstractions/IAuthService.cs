using SchoolPOS.Domain.Entities;
using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Domain.Abstractions;

/// <summary>Resultado de un intento de autenticación de operador.</summary>
public sealed record AuthResult(bool Succeeded, User? User, string? Error)
{
    public static AuthResult Ok(User user) => new(true, user, null);
    public static AuthResult Fail(string error) => new(false, null, error);
}

/// <summary>Autenticación y alta de operadores internos del POS con control de rol (FR-POS-1, FR-ADM-1).</summary>
public interface IAuthService
{
    /// <summary>
    /// Valida usuario y contraseña dentro de una escuela. Devuelve el operador si las credenciales
    /// son correctas y la cuenta está activa; en caso contrario, una falla genérica (sin revelar
    /// qué parte falló).
    /// </summary>
    Task<AuthResult> AuthenticateAsync(
        Guid schoolId, string username, string password, CancellationToken ct = default);

    /// <summary>Crea un operador con su contraseña hasheada.</summary>
    Task<User> CreateOperatorAsync(
        Guid schoolId, string username, string password, UserRole role, CancellationToken ct = default);
}
