using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Domain.Entities;

/// <summary>Factura de proveedor y su estado de pago (FR-PUR-4).</summary>
public class SupplierInvoice
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SchoolId { get; set; }

    public Guid SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

    /// <summary>Orden de compra asociada (opcional).</summary>
    public Guid? PurchaseOrderId { get; set; }

    public string InvoiceNumber { get; set; } = string.Empty;

    public DateTime IssueDate { get; set; }
    public DateTime? DueDate { get; set; }

    public decimal Amount { get; set; }
    public decimal AmountPaid { get; set; }

    public SupplierInvoiceStatus Status { get; set; } = SupplierInvoiceStatus.Pending;

    public DateTime CreatedAtUtc { get; set; }
}
