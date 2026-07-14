using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Domain.Entities;

/// <summary>Ingreso o egreso manual de efectivo no derivado de ventas (FR-TRE-2).</summary>
public class CashMovement
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CashSessionId { get; set; }
    public CashSession CashSession { get; set; } = null!;

    public CashMovementType Type { get; set; }

    /// <summary>Importe (siempre positivo; el tipo indica la dirección).</summary>
    public decimal Amount { get; set; }

    public string Reason { get; set; } = string.Empty;

    public Guid OperatorId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
