using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Domain.Entities;

/// <summary>
/// Asiento del Kardex: historial inmutable de entradas/salidas por producto (FR-INV-6).
/// La <see cref="Quantity"/> es con signo (+entrada / -salida), de modo que la suma reconcilia
/// con <see cref="Product.StockOnHand"/>. Análogo al libro mayor de saldo.
/// </summary>
public class StockMovement
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public StockMovementType Type { get; set; }

    /// <summary>Cantidad con signo (+entrada / -salida).</summary>
    public decimal Quantity { get; set; }

    /// <summary>Existencias resultantes tras el movimiento (instantánea).</summary>
    public decimal StockAfter { get; set; }

    /// <summary>Costo unitario en entradas (para valuación). Opcional en salidas.</summary>
    public decimal? UnitCost { get; set; }

    /// <summary>Referencia de origen (venta, recepción, ajuste).</summary>
    public string? Reference { get; set; }

    /// <summary>Motivo (merma, conteo, consumo interno).</summary>
    public string? Reason { get; set; }

    public Guid? OperatorId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
