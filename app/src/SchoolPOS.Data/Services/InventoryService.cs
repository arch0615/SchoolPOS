using Microsoft.EntityFrameworkCore;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Entities;
using SchoolPOS.Domain.Enums;
using SchoolPOS.Domain.Exceptions;

namespace SchoolPOS.Data.Services;

/// <summary>
/// Implementación atómica del inventario. Cada variación de existencias actualiza
/// <see cref="Product.StockOnHand"/> con un <c>UPDATE</c> y escribe un asiento inmutable en el
/// Kardex dentro de la misma transacción, de modo que la suma del Kardex reconcilia con el stock.
/// Las salidas usan un <c>UPDATE</c> condicional (<c>WHERE StockOnHand &gt;= cantidad</c>) para
/// impedir stock negativo. Componible con la transacción de una venta.
/// </summary>
public sealed class InventoryService : IInventoryService
{
    private readonly SchoolDbContext _db;
    private readonly IClock _clock;

    public InventoryService(SchoolDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public Task<StockMovement> RegisterEntryAsync(
        Guid productId, decimal quantity, decimal? unitCost, string? reference, Guid operatorId,
        CancellationToken ct = default)
    {
        RequirePositive(quantity);
        return _db.ExecuteAtomicAsync(async () =>
        {
            var now = _clock.UtcNow;
            var affected = await _db.Products
                .Where(p => p.Id == productId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.StockOnHand, p => p.StockOnHand + quantity), ct);
            if (affected == 0)
                throw new InvalidOperationException($"Producto {productId} no encontrado.");

            return await AppendMovementAsync(
                productId, StockMovementType.Entry, quantity, unitCost, reference, reason: null, operatorId, now, ct);
        }, ct);
    }

    public Task<StockMovement> RegisterExitAsync(
        Guid productId, decimal quantity, string reason, string? reference, Guid operatorId,
        CancellationToken ct = default)
    {
        RequirePositive(quantity);
        return _db.ExecuteAtomicAsync(async () =>
        {
            var now = _clock.UtcNow;
            // Condicional: solo descuenta si hay existencias suficientes (sin stock negativo).
            var affected = await _db.Products
                .Where(p => p.Id == productId && p.StockOnHand >= quantity)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.StockOnHand, p => p.StockOnHand - quantity), ct);

            if (affected == 0)
            {
                var product = await _db.Products.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == productId, ct)
                    ?? throw new InvalidOperationException($"Producto {productId} no encontrado.");
                throw new InsufficientStockException(productId, quantity, product.StockOnHand);
            }

            return await AppendMovementAsync(
                productId, StockMovementType.Exit, -quantity, unitCost: null, reference, reason, operatorId, now, ct);
        }, ct);
    }

    public Task<StockMovement> AdjustToCountAsync(
        Guid productId, decimal countedQuantity, string reason, Guid operatorId, CancellationToken ct = default)
    {
        if (countedQuantity < 0m)
            throw new ArgumentOutOfRangeException(nameof(countedQuantity), "El conteo no puede ser negativo.");

        return _db.ExecuteAtomicAsync(async () =>
        {
            var now = _clock.UtcNow;
            var current = await _db.Products.AsNoTracking()
                .Where(p => p.Id == productId).Select(p => (decimal?)p.StockOnHand).FirstOrDefaultAsync(ct)
                ?? throw new InvalidOperationException($"Producto {productId} no encontrado.");

            var delta = countedQuantity - current;
            await _db.Products
                .Where(p => p.Id == productId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.StockOnHand, countedQuantity), ct);

            return await AppendMovementAsync(
                productId, StockMovementType.Adjustment, delta, unitCost: null, reference: null, reason, operatorId, now, ct);
        }, ct);
    }

    public async Task<IReadOnlyList<Product>> GetLowStockAsync(Guid schoolId, CancellationToken ct = default) =>
        await _db.Products.AsNoTracking()
            .Where(p => p.SchoolId == schoolId && p.IsActive && p.StockOnHand <= p.MinStock)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

    private async Task<StockMovement> AppendMovementAsync(
        Guid productId, StockMovementType type, decimal signedQty, decimal? unitCost,
        string? reference, string? reason, Guid operatorId, DateTime now, CancellationToken ct)
    {
        var stockAfter = await _db.Products.AsNoTracking()
            .Where(p => p.Id == productId).Select(p => p.StockOnHand).FirstAsync(ct);

        var movement = new StockMovement
        {
            ProductId = productId,
            Type = type,
            Quantity = signedQty,
            StockAfter = stockAfter,
            UnitCost = unitCost,
            Reference = reference,
            Reason = reason,
            OperatorId = operatorId,
            CreatedAtUtc = now,
        };
        _db.StockMovements.Add(movement);
        await _db.SaveChangesAsync(ct);
        return movement;
    }

    private static void RequirePositive(decimal quantity)
    {
        if (quantity <= 0m)
            throw new ArgumentOutOfRangeException(nameof(quantity), "La cantidad debe ser positiva.");
    }
}
