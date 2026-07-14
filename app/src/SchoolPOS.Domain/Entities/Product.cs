namespace SchoolPOS.Domain.Entities;

/// <summary>
/// Producto del catálogo (FR-INV-1). Guarda precio y <b>costo</b>, código de barras y stock.
/// El <see cref="StockOnHand"/> se mantiene denormalizado y se actualiza atómicamente junto con
/// cada asiento de <see cref="StockMovement"/> (mismo patrón que el saldo/libro mayor).
/// </summary>
public class Product
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SchoolId { get; set; }

    public Guid? CategoryId { get; set; }
    public Category? Category { get; set; }

    /// <summary>Código de barras. Único por escuela (opcional).</summary>
    public string? Barcode { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Precio de venta.</summary>
    public decimal Price { get; set; }

    /// <summary>Costo (para márgenes y valuación de inventario).</summary>
    public decimal Cost { get; set; }

    /// <summary>Existencias actuales (denormalizado; se mueve con el Kardex).</summary>
    public decimal StockOnHand { get; set; }

    /// <summary>Mínimo para disparar alerta de bajo inventario (FR-INV-5).</summary>
    public decimal MinStock { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<StockMovement> StockMovements { get; set; } = new List<StockMovement>();

    public DateTime CreatedAtUtc { get; set; }
}
