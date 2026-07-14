using Microsoft.EntityFrameworkCore;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Entities;
using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Data.Services;

/// <summary>
/// Implementación del servicio de compras. La recepción de mercancía reutiliza
/// <see cref="IInventoryService"/> dentro de una sola transacción, de modo que las existencias
/// suben con su asiento de Kardex y la orden refleja lo recibido de forma consistente.
/// </summary>
public sealed class PurchasingService : IPurchasingService
{
    private readonly SchoolDbContext _db;
    private readonly IInventoryService _inventory;
    private readonly IClock _clock;

    private const int Scale = 2;
    private const MidpointRounding Rounding = MidpointRounding.AwayFromZero;

    public PurchasingService(SchoolDbContext db, IInventoryService inventory, IClock clock)
    {
        _db = db;
        _inventory = inventory;
        _clock = clock;
    }

    public Task<PurchaseOrder> CreateOrderAsync(
        Guid schoolId, Guid supplierId, string orderNumber,
        IReadOnlyList<PurchaseOrderLineRequest> lines, DateTime? expectedDate, string? notes,
        CancellationToken ct = default)
    {
        if (lines.Count == 0)
            throw new ArgumentException("La orden no tiene renglones.", nameof(lines));

        return _db.ExecuteAtomicAsync(async () =>
        {
            var now = _clock.UtcNow;
            var poLines = lines.Select(l => new PurchaseOrderLine
            {
                ProductId = l.ProductId,
                Quantity = l.Quantity,
                UnitCost = l.UnitCost,
                LineTotal = Round(l.Quantity * l.UnitCost),
            }).ToList();

            var order = new PurchaseOrder
            {
                SchoolId = schoolId,
                SupplierId = supplierId,
                OrderNumber = orderNumber,
                Status = PurchaseOrderStatus.Draft,
                Total = Round(poLines.Sum(l => l.LineTotal)),
                OrderDate = now,
                ExpectedDate = expectedDate,
                Notes = notes,
                Lines = poLines,
                CreatedAtUtc = now,
            };
            _db.PurchaseOrders.Add(order);
            await _db.SaveChangesAsync(ct);
            return order;
        }, ct);
    }

    public Task<PurchaseOrder> MarkSentAsync(Guid orderId, CancellationToken ct = default) =>
        _db.ExecuteAtomicAsync(async () =>
        {
            var order = await _db.PurchaseOrders.FirstOrDefaultAsync(o => o.Id == orderId, ct)
                ?? throw new InvalidOperationException($"Orden {orderId} no encontrada.");
            if (order.Status != PurchaseOrderStatus.Draft)
                throw new InvalidOperationException($"Solo una orden en Borrador puede enviarse (estado actual: {order.Status}).");

            order.Status = PurchaseOrderStatus.Sent;
            await _db.SaveChangesAsync(ct);
            return order;
        }, ct);

    public Task<GoodsReceipt> ReceiveGoodsAsync(
        Guid orderId, IReadOnlyList<ReceiptLineRequest> lines, Guid receivedByUserId, string? notes,
        CancellationToken ct = default)
    {
        if (lines.Count == 0)
            throw new ArgumentException("No se indicaron renglones a recibir.", nameof(lines));

        return _db.ExecuteAtomicAsync(async () =>
        {
            var now = _clock.UtcNow;
            var order = await _db.PurchaseOrders.Include(o => o.Lines)
                .FirstOrDefaultAsync(o => o.Id == orderId, ct)
                ?? throw new InvalidOperationException($"Orden {orderId} no encontrada.");
            if (order.Status is PurchaseOrderStatus.Cancelled or PurchaseOrderStatus.Received)
                throw new InvalidOperationException($"No se puede recibir sobre una orden {order.Status}.");

            var receipt = new GoodsReceipt
            {
                SchoolId = order.SchoolId,
                PurchaseOrderId = order.Id,
                ReceivedByUserId = receivedByUserId,
                ReceivedAtUtc = now,
                Notes = notes,
            };
            _db.GoodsReceipts.Add(receipt);
            await _db.SaveChangesAsync(ct);

            foreach (var line in lines)
            {
                if (line.Quantity <= 0m)
                    throw new ArgumentException("La cantidad recibida debe ser positiva.", nameof(lines));

                // Suma existencias con su asiento de Kardex.
                await _inventory.RegisterEntryAsync(
                    line.ProductId, line.Quantity, line.UnitCost, $"REC:{order.Id}", receivedByUserId, ct);

                _db.GoodsReceiptLines.Add(new GoodsReceiptLine
                {
                    GoodsReceiptId = receipt.Id,
                    ProductId = line.ProductId,
                    PurchaseOrderLineId = line.PurchaseOrderLineId,
                    QuantityReceived = line.Quantity,
                    UnitCost = line.UnitCost,
                });

                // Acumula lo recibido en el renglón de la orden (por Id o por producto).
                var poLine = line.PurchaseOrderLineId is not null
                    ? order.Lines.FirstOrDefault(pl => pl.Id == line.PurchaseOrderLineId)
                    : order.Lines.FirstOrDefault(pl => pl.ProductId == line.ProductId);
                if (poLine is not null)
                    poLine.QuantityReceived += line.Quantity;
            }

            order.Status = order.Lines.All(pl => pl.QuantityReceived >= pl.Quantity)
                ? PurchaseOrderStatus.Received
                : PurchaseOrderStatus.PartiallyReceived;

            await _db.SaveChangesAsync(ct);
            return receipt;
        }, ct);
    }

    public Task<SupplierInvoice> RegisterInvoiceAsync(
        Guid schoolId, Guid supplierId, Guid? orderId, string invoiceNumber, decimal amount,
        DateTime issueDate, DateTime? dueDate, CancellationToken ct = default) =>
        _db.ExecuteAtomicAsync(async () =>
        {
            var invoice = new SupplierInvoice
            {
                SchoolId = schoolId,
                SupplierId = supplierId,
                PurchaseOrderId = orderId,
                InvoiceNumber = invoiceNumber,
                Amount = Round(amount),
                AmountPaid = 0m,
                Status = SupplierInvoiceStatus.Pending,
                IssueDate = issueDate,
                DueDate = dueDate,
                CreatedAtUtc = _clock.UtcNow,
            };
            _db.SupplierInvoices.Add(invoice);
            await _db.SaveChangesAsync(ct);
            return invoice;
        }, ct);

    public Task<SupplierInvoice> RegisterInvoicePaymentAsync(
        Guid invoiceId, decimal amountPaid, CancellationToken ct = default)
    {
        if (amountPaid <= 0m)
            throw new ArgumentOutOfRangeException(nameof(amountPaid), "El pago debe ser positivo.");

        return _db.ExecuteAtomicAsync(async () =>
        {
            var invoice = await _db.SupplierInvoices.FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
                ?? throw new InvalidOperationException($"Factura {invoiceId} no encontrada.");

            invoice.AmountPaid = Round(invoice.AmountPaid + amountPaid);
            invoice.Status = invoice.AmountPaid >= invoice.Amount
                ? SupplierInvoiceStatus.Paid
                : SupplierInvoiceStatus.PartiallyPaid;

            await _db.SaveChangesAsync(ct);
            return invoice;
        }, ct);
    }

    private static decimal Round(decimal value) => Math.Round(value, Scale, Rounding);
}
