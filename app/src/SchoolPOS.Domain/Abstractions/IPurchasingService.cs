using SchoolPOS.Domain.Entities;

namespace SchoolPOS.Domain.Abstractions;

/// <summary>Renglón solicitado al crear una orden de compra.</summary>
public sealed record PurchaseOrderLineRequest(Guid ProductId, decimal Quantity, decimal UnitCost);

/// <summary>Renglón recibido en una recepción de mercancía.</summary>
public sealed record ReceiptLineRequest(
    Guid ProductId, decimal Quantity, decimal UnitCost, Guid? PurchaseOrderLineId = null);

/// <summary>
/// Servicio de compras (FR-PUR). Gestiona órdenes de compra, la recepción de mercancía que
/// <b>actualiza el inventario automáticamente</b> (entradas al Kardex vía inventario) y las
/// facturas de proveedor con su estado de pago. La recepción es atómica.
/// </summary>
public interface IPurchasingService
{
    /// <summary>Crea una orden de compra en estado Borrador y calcula su total (FR-PUR-2).</summary>
    Task<PurchaseOrder> CreateOrderAsync(
        Guid schoolId, Guid supplierId, string orderNumber,
        IReadOnlyList<PurchaseOrderLineRequest> lines, DateTime? expectedDate, string? notes,
        CancellationToken ct = default);

    /// <summary>Marca la orden como enviada al proveedor.</summary>
    Task<PurchaseOrder> MarkSentAsync(Guid orderId, CancellationToken ct = default);

    /// <summary>
    /// Registra la recepción de mercancía contra una orden (FR-PUR-3): suma existencias por cada
    /// renglón (Kardex), actualiza lo recibido en la orden y ajusta su estado
    /// (Parcial/Recibida). Todo en una transacción.
    /// </summary>
    Task<GoodsReceipt> ReceiveGoodsAsync(
        Guid orderId, IReadOnlyList<ReceiptLineRequest> lines, Guid receivedByUserId, string? notes,
        CancellationToken ct = default);

    /// <summary>Registra una factura de proveedor (FR-PUR-4).</summary>
    Task<SupplierInvoice> RegisterInvoiceAsync(
        Guid schoolId, Guid supplierId, Guid? orderId, string invoiceNumber, decimal amount,
        DateTime issueDate, DateTime? dueDate, CancellationToken ct = default);

    /// <summary>Aplica un pago a la factura y actualiza su estado (Pendiente/Parcial/Pagada).</summary>
    Task<SupplierInvoice> RegisterInvoicePaymentAsync(
        Guid invoiceId, decimal amountPaid, CancellationToken ct = default);
}
