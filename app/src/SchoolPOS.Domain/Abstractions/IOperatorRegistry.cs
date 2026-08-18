using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Domain.Abstractions;

/// <summary>Operador del POS para la pantalla de administración.</summary>
public sealed record OperatorRow(
    Guid UserId,
    string Username,
    UserRole Role,
    bool IsActive,
    bool IsLockedOut,
    DateTime CreatedAtUtc);

/// <summary>
/// Alta y mantenimiento de los operadores del POS (FR-ADM-1). El asistente de instalación crea un
/// único administrador; sin esta pantalla la escuela no podía dar de alta a sus cajeros, así que
/// el modelo de roles existía pero nadie podía usarlo.
/// </summary>
public interface IOperatorRegistry
{
    Task<IReadOnlyList<OperatorRow>> ListAsync(
        Guid schoolId, bool includeInactive = false, CancellationToken ct = default);

    /// <summary>Cambia el rol de un operador.</summary>
    Task SetRoleAsync(Guid userId, UserRole role, CancellationToken ct = default);

    /// <summary>
    /// Da de baja o reactiva. Baja lógica: sus ventas y asientos referencian este operador y deben
    /// seguir siendo atribuibles.
    /// </summary>
    Task SetActiveAsync(Guid userId, bool isActive, CancellationToken ct = default);

    /// <summary>Asigna una contraseña nueva (el administrador la entrega al operador).</summary>
    Task ResetPasswordAsync(Guid userId, string newPassword, CancellationToken ct = default);

    /// <summary>Levanta el bloqueo por intentos fallidos sin esperar los 15 minutos.</summary>
    Task UnlockAsync(Guid userId, CancellationToken ct = default);
}
