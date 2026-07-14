using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Domain.Entities;

/// <summary>
/// Sesión de caja: apertura con fondo inicial y cierre con arqueo (FR-TRE-1). La variación es
/// el conteo real menos el esperado (fondo + ingresos - egresos + ventas en efectivo).
/// </summary>
public class CashSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SchoolId { get; set; }

    public Guid OperatorId { get; set; }

    public CashSessionStatus Status { get; set; } = CashSessionStatus.Open;

    /// <summary>Fondo inicial de caja.</summary>
    public decimal OpeningFloat { get; set; }

    /// <summary>Monto contado al cierre (arqueo).</summary>
    public decimal? CountedAmount { get; set; }

    /// <summary>Monto esperado por el sistema al cierre.</summary>
    public decimal? ExpectedAmount { get; set; }

    /// <summary>Diferencia = CountedAmount - ExpectedAmount (FR-TRE-3).</summary>
    public decimal? Variance { get; set; }

    public DateTime OpenedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }

    public ICollection<CashMovement> Movements { get; set; } = new List<CashMovement>();
}
