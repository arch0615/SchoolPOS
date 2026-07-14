using SchoolPOS.Domain.Entities;

namespace SchoolPOS.Domain.Abstractions;

/// <summary>
/// Servicio de inventario. Toda variación de existencias escribe un asiento inmutable en el
/// Kardex y actualiza <see cref="Product.StockOnHand"/> de forma atómica (mismo patrón que el
/// saldo). Las salidas se bloquean si no hay existencias suficientes (sin stock negativo).
/// </summary>
public interface IInventoryService
{
    /// <summary>Entrada de mercancía (FR-INV-3). Suma existencias y registra el costo unitario.</summary>
    Task<StockMovement> RegisterEntryAsync(
        Guid productId, decimal quantity, decimal? unitCost, string? reference, Guid operatorId,
        CancellationToken ct = default);

    /// <summary>
    /// Salida de mercancía (venta, merma, consumo interno), FR-INV-4. Resta existencias; lanza
    /// <see cref="Exceptions.InsufficientStockException"/> si no alcanza el stock.
    /// </summary>
    Task<StockMovement> RegisterExitAsync(
        Guid productId, decimal quantity, string reason, string? reference, Guid operatorId,
        CancellationToken ct = default);

    /// <summary>
    /// Ajuste por conteo físico (FR-INV-4): fija las existencias al valor contado y registra la
    /// diferencia con signo en el Kardex. Puede subir o bajar el stock.
    /// </summary>
    Task<StockMovement> AdjustToCountAsync(
        Guid productId, decimal countedQuantity, string reason, Guid operatorId,
        CancellationToken ct = default);

    /// <summary>Productos por debajo de su mínimo (alertas de bajo inventario, FR-INV-5).</summary>
    Task<IReadOnlyList<Product>> GetLowStockAsync(Guid schoolId, CancellationToken ct = default);
}
