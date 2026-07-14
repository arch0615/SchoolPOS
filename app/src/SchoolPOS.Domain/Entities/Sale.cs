using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Domain.Entities;

/// <summary>
/// Venta del POS (FR-SAL-1). Se cobra contra el saldo del estudiante o en efectivo
/// (<see cref="Tender"/>). Cuando el cobro es por saldo, genera un asiento en el libro mayor
/// cuya referencia es el Id de esta venta.
/// </summary>
public class Sale
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SchoolId { get; set; }

    /// <summary>Cajero que realizó la venta.</summary>
    public Guid CashierId { get; set; }

    /// <summary>Estudiante (cuando el cobro es contra saldo). Nulo en venta de mostrador en efectivo.</summary>
    public Guid? StudentId { get; set; }
    public Guid? AccountId { get; set; }

    /// <summary>Sesión de caja abierta (para ventas en efectivo / arqueo).</summary>
    public Guid? CashSessionId { get; set; }

    public TenderType Tender { get; set; }
    public SaleStatus Status { get; set; } = SaleStatus.Completed;

    public decimal Subtotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal Total { get; set; }

    public ICollection<SaleLine> Lines { get; set; } = new List<SaleLine>();

    public DateTime CreatedAtUtc { get; set; }
}
