namespace SchoolPOS.Domain.Abstractions;

/// <summary>Renglón de la bitácora para el visor de auditoría (FR-ADM-4).</summary>
public sealed record AuditEntryRow(
    DateTime CreatedAtUtc,
    string Actor,
    string Action,
    string Entity,
    string? EntityId,
    string? Before,
    string? After);

/// <summary>
/// Consulta de la bitácora de acciones sensibles (ajustes de saldo, devoluciones, cambios de
/// precio), FR-ADM-4. Solo lectura; los registros son inmutables.
/// </summary>
public interface IAuditLogQueryService
{
    /// <summary>Consulta la bitácora filtrando por rango de fechas y, opcionalmente, por acción.</summary>
    Task<IReadOnlyList<AuditEntryRow>> QueryAsync(
        Guid schoolId, DateTime? fromUtc, DateTime? toUtc, string? action, CancellationToken ct = default);
}
