namespace SchoolPOS.Domain.Entities;

/// <summary>Renglón recibido de una recepción de mercancía.</summary>
public class GoodsReceiptLine
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid GoodsReceiptId { get; set; }
    public GoodsReceipt GoodsReceipt { get; set; } = null!;

    public Guid ProductId { get; set; }

    /// <summary>Renglón de la orden que se surte (para conciliar pedido vs. recibido).</summary>
    public Guid? PurchaseOrderLineId { get; set; }

    public decimal QuantityReceived { get; set; }
    public decimal UnitCost { get; set; }
}
