namespace SchoolPOS.Domain.Entities;

/// <summary>Renglón de una orden de compra. Rastrea lo pedido vs. lo recibido (FR-PUR-3).</summary>
public class PurchaseOrderLine
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PurchaseOrderId { get; set; }
    public PurchaseOrder PurchaseOrder { get; set; } = null!;

    public Guid ProductId { get; set; }

    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal LineTotal { get; set; }

    /// <summary>Cantidad recibida acumulada (para recepciones parciales).</summary>
    public decimal QuantityReceived { get; set; }
}
