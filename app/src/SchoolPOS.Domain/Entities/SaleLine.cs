namespace SchoolPOS.Domain.Entities;

/// <summary>
/// Renglón de venta. Guarda instantánea del nombre y precio del producto al momento de la venta
/// (para que reportes/recibos no cambien si luego se edita el catálogo). La suma de
/// <see cref="LineTotal"/> de los renglones cuadra con <see cref="Sale.Total"/> (DoD 1.7).
/// </summary>
public class SaleLine
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SaleId { get; set; }
    public Sale Sale { get; set; } = null!;

    public Guid ProductId { get; set; }

    /// <summary>Nombre del producto al momento de la venta (instantánea).</summary>
    public string Description { get; set; } = string.Empty;

    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }

    /// <summary>Descuento aplicado al renglón (FR-SAL-3).</summary>
    public decimal Discount { get; set; }

    /// <summary>Total del renglón = Quantity * UnitPrice - Discount.</summary>
    public decimal LineTotal { get; set; }

    /// <summary>Cantidad ya devuelta de este renglón (para devoluciones parciales, FR-SAL-5).</summary>
    public decimal QuantityRefunded { get; set; }
}
