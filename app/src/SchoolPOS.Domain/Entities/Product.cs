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

    /// <summary>Visible en el catálogo del portal de papás (pedidos anticipados). Por omisión,
    /// oculto: un producto nuevo no aparece a los tutores hasta que la escuela lo habilite.</summary>
    public bool ShowInPortal { get; set; }

    /// <summary>
    /// Día de la semana en que este producto es "el menú" (comida corrida que cambia cada
    /// semana). La escuela reutiliza el mismo producto y solo edita nombre/precio cuando cambia
    /// el menú — no crea uno nuevo cada semana. <c>null</c> = no es un producto de menú.
    /// </summary>
    public DayOfWeek? MenuDayOfWeek { get; set; }

    public ICollection<StockMovement> StockMovements { get; set; } = new List<StockMovement>();

    public DateTime CreatedAtUtc { get; set; }
}
