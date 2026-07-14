using Microsoft.EntityFrameworkCore;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Entities;
using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Data.Services;

/// <summary>
/// Implementación del servicio de ventas. Registra la venta, descuenta inventario y (para cobro
/// por saldo) aplica el cargo al libro mayor, todo en una sola transacción atómica. Reutiliza
/// <see cref="IInventoryService"/> y <see cref="IBalanceService"/>, que participan en la misma
/// transacción (no anidan). Si el stock o el saldo no alcanzan, se revierte toda la venta.
/// </summary>
public sealed class SalesService : ISalesService
{
    private readonly SchoolDbContext _db;
    private readonly IInventoryService _inventory;
    private readonly IBalanceService _balance;
    private readonly IClock _clock;

    private const int Scale = 2;
    private const MidpointRounding Rounding = MidpointRounding.AwayFromZero;

    public SalesService(SchoolDbContext db, IInventoryService inventory, IBalanceService balance, IClock clock)
    {
        _db = db;
        _inventory = inventory;
        _balance = balance;
        _clock = clock;
    }

    public Task<Sale> RegisterSaleAsync(SaleRequest request, CancellationToken ct = default)
    {
        if (request.Lines.Count == 0)
            throw new ArgumentException("La venta no tiene renglones.", nameof(request));
        if (request.Tender == TenderType.Balance && request.AccountId is null)
            throw new ArgumentException("El cobro por saldo requiere una cuenta.", nameof(request));

        return _db.ExecuteAtomicAsync(async () =>
        {
            var now = _clock.UtcNow;
            var school = await _db.Schools.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.SchoolId, ct)
                ?? throw new InvalidOperationException($"Escuela {request.SchoolId} no encontrada.");

            var lines = request.Lines.Select(l => new SaleLine
            {
                ProductId = l.ProductId,
                Description = l.Description,
                Quantity = l.Quantity,
                UnitPrice = l.UnitPrice,
                Discount = Round(l.Discount),
                LineTotal = Round(l.Quantity * l.UnitPrice - l.Discount),
            }).ToList();

            var (subtotal, discountTotal, taxTotal, total) =
                ComputeTotals(lines, school.TaxRate, school.TaxInclusive);

            var sale = new Sale
            {
                SchoolId = request.SchoolId,
                CashierId = request.CashierId,
                StudentId = request.StudentId,
                AccountId = request.AccountId,
                CashSessionId = request.CashSessionId,
                Tender = request.Tender,
                Status = SaleStatus.Completed,
                Subtotal = subtotal,
                DiscountTotal = discountTotal,
                TaxTotal = taxTotal,
                Total = total,
                Lines = lines,
                CreatedAtUtc = now,
            };
            _db.Sales.Add(sale);
            await _db.SaveChangesAsync(ct);

            // Descontar inventario por cada renglón (bloquea si no hay existencias).
            foreach (var line in lines)
            {
                await _inventory.RegisterExitAsync(
                    line.ProductId, line.Quantity, reason: "Venta",
                    reference: sale.Id.ToString(), operatorId: request.CashierId, ct);
            }

            // Cobro por saldo: debitar la cuenta (bloquea si no alcanza).
            if (request.Tender == TenderType.Balance)
            {
                await _balance.ChargeSaleAsync(
                    request.AccountId!.Value, total, sale.Id.ToString(), request.CashierId, ct);
            }

            return sale;
        }, ct);
    }

    public Task<Sale> RefundSaleAsync(
        Guid saleId, IReadOnlyList<(Guid SaleLineId, decimal Quantity)> lines, Guid operatorId,
        CancellationToken ct = default)
    {
        if (lines.Count == 0)
            throw new ArgumentException("No se indicaron renglones a devolver.", nameof(lines));

        return _db.ExecuteAtomicAsync(async () =>
        {
            var now = _clock.UtcNow;
            var sale = await _db.Sales.Include(s => s.Lines).FirstOrDefaultAsync(s => s.Id == saleId, ct)
                ?? throw new InvalidOperationException($"Venta {saleId} no encontrada.");

            decimal totalRefund = 0m;
            foreach (var (saleLineId, qty) in lines)
            {
                if (qty <= 0m)
                    throw new ArgumentException("La cantidad a devolver debe ser positiva.", nameof(lines));

                var line = sale.Lines.FirstOrDefault(l => l.Id == saleLineId)
                    ?? throw new InvalidOperationException($"Renglón {saleLineId} no pertenece a la venta {saleId}.");

                var refundable = line.Quantity - line.QuantityRefunded;
                if (qty > refundable)
                    throw new InvalidOperationException(
                        $"No se pueden devolver {qty}; disponibles {refundable} en el renglón {saleLineId}.");

                // Importe proporcional del renglón (ya neto de descuento).
                var unitNet = line.Quantity == 0m ? 0m : line.LineTotal / line.Quantity;
                totalRefund += Round(qty * unitNet);

                line.QuantityRefunded += qty;

                // Reingresar stock.
                await _inventory.RegisterEntryAsync(
                    line.ProductId, qty, unitCost: null, reference: $"DEV:{sale.Id}", operatorId, ct);
            }

            totalRefund = Round(totalRefund);

            // Reintegrar saldo si la venta se cobró por saldo.
            if (sale.Tender == TenderType.Balance && sale.AccountId is not null && totalRefund > 0m)
            {
                await _balance.RefundAsync(sale.AccountId.Value, totalRefund, $"DEV:{sale.Id}", operatorId, ct);
            }

            // Estado de la venta según lo devuelto.
            sale.Status = sale.Lines.All(l => l.QuantityRefunded >= l.Quantity)
                ? SaleStatus.Refunded
                : SaleStatus.PartiallyRefunded;

            _db.AuditLogs.Add(new AuditLog
            {
                SchoolId = sale.SchoolId,
                Actor = operatorId.ToString(),
                Action = "Refund",
                Entity = nameof(Sale),
                EntityId = sale.Id.ToString(),
                After = $"Devolución {totalRefund:0.00} ({sale.Status})",
                CreatedAtUtc = now,
            });

            await _db.SaveChangesAsync(ct);
            return sale;
        }, ct);
    }

    /// <summary>Calcula subtotal, descuento, impuesto y total según la configuración de la escuela.</summary>
    private static (decimal Subtotal, decimal DiscountTotal, decimal TaxTotal, decimal Total) ComputeTotals(
        IEnumerable<SaleLine> lines, decimal taxRate, bool taxInclusive)
    {
        var list = lines.ToList();
        var subtotal = Round(list.Sum(l => l.Quantity * l.UnitPrice));
        var discountTotal = Round(list.Sum(l => l.Discount));
        var net = subtotal - discountTotal;

        decimal taxTotal;
        decimal total;
        if (taxRate <= 0m)
        {
            taxTotal = 0m;
            total = net;
        }
        else if (taxInclusive)
        {
            // El impuesto ya está contenido en el precio: se extrae la porción.
            total = net;
            taxTotal = Round(net - net / (1m + taxRate));
        }
        else
        {
            taxTotal = Round(net * taxRate);
            total = net + taxTotal;
        }

        return (subtotal, discountTotal, taxTotal, total);
    }

    private static decimal Round(decimal value) => Math.Round(value, Scale, Rounding);
}
