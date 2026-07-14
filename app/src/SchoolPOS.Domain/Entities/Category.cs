namespace SchoolPOS.Domain.Entities;

/// <summary>Categoría para organizar productos por tipo (FR-INV-2).</summary>
public class Category
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SchoolId { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }
}
