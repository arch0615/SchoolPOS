namespace SchoolPOS.Domain.Entities;

/// <summary>Proveedor (FR-PUR-1).</summary>
public class Supplier
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SchoolId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>RFC fiscal (México). Opcional.</summary>
    public string? Rfc { get; set; }

    public string? ContactName { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }
}
