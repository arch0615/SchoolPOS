namespace SchoolPOS.Domain.Entities;

/// <summary>
/// Bitácora de acciones sensibles (ajustes de saldo, devoluciones, cambios de precio),
/// FR-ADM-4 / NFR-5. Inmutable; guarda estado antes/después para trazabilidad.
/// </summary>
public class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SchoolId { get; set; }

    /// <summary>Operador o sistema que ejecutó la acción.</summary>
    public string Actor { get; set; } = string.Empty;

    /// <summary>Acción realizada (p. ej. "BalanceAdjustment", "Refund", "PriceChange").</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Entidad afectada (p. ej. "Account").</summary>
    public string Entity { get; set; } = string.Empty;

    public string? EntityId { get; set; }

    public string? Before { get; set; }
    public string? After { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
