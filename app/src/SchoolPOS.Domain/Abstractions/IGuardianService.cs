using SchoolPOS.Domain.Entities;

namespace SchoolPOS.Domain.Abstractions;

/// <summary>Resultado de autenticación de un tutor en el portal.</summary>
public sealed record GuardianAuthResult(bool Succeeded, Guardian? Guardian, string? Error, bool IsLockedOut)
{
    public static GuardianAuthResult Ok(Guardian g) => new(true, g, null, false);
    public static GuardianAuthResult Fail(string error) => new(false, null, error, false);
    public static GuardianAuthResult Locked(string error) => new(false, null, error, true);
}

/// <summary>Estudiante vinculado a un tutor, con su saldo (para el panel del portal).</summary>
public sealed record LinkedStudent(
    Guid StudentId, Guid AccountId, string EnrollmentNo, string FullName, decimal Balance);

/// <summary>Movimiento del libro mayor para el historial del portal.</summary>
public sealed record MovementRow(
    DateTime CreatedAtUtc, string Type, decimal Amount, decimal BalanceAfter, string? Reference);

/// <summary>
/// Servicio del portal para tutores/padres (FR-WP): registro, inicio de sesión con bloqueo por
/// intentos fallidos, vinculación de estudiantes por matrícula y consultas de saldo/movimientos.
/// </summary>
public interface IGuardianService
{
    /// <summary>Registra un tutor con contraseña hasheada (FR-WP-2). Email único por escuela.</summary>
    Task<Guardian> RegisterAsync(
        Guid schoolId, string email, string password, string fullName, CancellationToken ct = default);

    /// <summary>
    /// Autentica al tutor con bloqueo tras N intentos fallidos (FR-WP-3). Devuelve un resultado
    /// bloqueado si la cuenta está temporalmente bloqueada.
    /// </summary>
    Task<GuardianAuthResult> AuthenticateAsync(
        Guid schoolId, string email, string password, CancellationToken ct = default);

    /// <summary>Vincula un estudiante al tutor por número de matrícula (FR-WP-9).</summary>
    Task LinkStudentByEnrollmentAsync(
        Guid guardianId, Guid schoolId, string enrollmentNo, CancellationToken ct = default);

    /// <summary>Estudiantes vinculados al tutor con su saldo actual.</summary>
    Task<IReadOnlyList<LinkedStudent>> GetLinkedStudentsAsync(Guid guardianId, CancellationToken ct = default);

    /// <summary>Confirma que un estudiante pertenece al tutor (control de acceso a su cuenta).</summary>
    Task<bool> OwnsStudentAsync(Guid guardianId, Guid studentId, CancellationToken ct = default);

    /// <summary>Movimientos de una cuenta, opcionalmente filtrados por rango de fechas (FR-WP-8).</summary>
    Task<IReadOnlyList<MovementRow>> GetMovementsAsync(
        Guid accountId, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default);
}
