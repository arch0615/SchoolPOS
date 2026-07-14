namespace SchoolPOS.Domain.Entities;

/// <summary>
/// Recepción de mercancía contra una orden de compra. Al confirmarse, actualiza el inventario
/// automáticamente (entradas al Kardex), FR-PUR-3.
/// </summary>
public class GoodsReceipt
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SchoolId { get; set; }

    public Guid PurchaseOrderId { get; set; }
    public PurchaseOrder PurchaseOrder { get; set; } = null!;

    public Guid ReceivedByUserId { get; set; }

    public DateTime ReceivedAtUtc { get; set; }
    public string? Notes { get; set; }

    public ICollection<GoodsReceiptLine> Lines { get; set; } = new List<GoodsReceiptLine>();
}
