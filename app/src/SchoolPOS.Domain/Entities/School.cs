namespace SchoolPOS.Domain.Entities;

/// <summary>
/// Escuela / Tenant. Una instalación por escuela (DB local). Guarda los parámetros
/// configurables por escuela: comisión, moneda e impuesto (requirements §6).
/// </summary>
public class School
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    /// <summary>Tasa de comisión sobre recargas en línea. Default 0.05, puede ser 0 (FR-COM-6).</summary>
    public decimal CommissionRate { get; set; } = 0.05m;

    /// <summary>Moneda ISO única por escuela (MXN/USD). Una sola moneda por escuela (Q8).</summary>
    public string Currency { get; set; } = "MXN";

    /// <summary>Tasa de IVA configurable por escuela (FR-ADM-3 / Q7).</summary>
    public decimal TaxRate { get; set; } = 0m;

    /// <summary>Si el impuesto está incluido en el precio (true) o se añade (false).</summary>
    public bool TaxInclusive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }
}
