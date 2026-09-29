namespace SchoolPOS.Domain.Entities;

/// <summary>
/// Imagen del menú semanal que la escuela sube desde el portal, mostrada como banner encima del
/// catálogo de pedidos anticipados (FR-WP) — puramente decorativa/informativa, no lo que impulsa
/// el pedido en sí (eso sigue siendo los productos con <see cref="Product.ShowInPortal"/>/
/// <see cref="Product.MenuDayOfWeek"/>, que sí se pueden cobrar). Una por escuela; subir una nueva
/// reemplaza la anterior.
/// </summary>
public class SchoolMenuImage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SchoolId { get; set; }
    public byte[] ImageBytes { get; set; } = Array.Empty<byte>();
    public string ContentType { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
}
