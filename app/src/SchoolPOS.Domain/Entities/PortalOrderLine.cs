namespace SchoolPOS.Domain.Entities;

/// <summary>Renglón de un <see cref="PortalOrder"/>. Instantánea de nombre/precio, igual que <see cref="SaleLine"/>.</summary>
public class PortalOrderLine
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OrderId { get; set; }
    public PortalOrder Order { get; set; } = null!;

    public Guid ProductId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}
