namespace SchoolPOS.Domain.Abstractions;

/// <summary>
/// Parámetros fiscales para la factura de comisión. <b>Confirmar con el contador</b>: bajo el
/// régimen y modelo del proveedor, el IVA y si el importe de comisión ya lo incluye.
/// </summary>
public sealed class CfdiSettings
{
    /// <summary>IVA aplicable a la comisión (0.16 por defecto; 0 si exento).</summary>
    public decimal TaxRate { get; set; } = 0.16m;

    /// <summary>Si el importe de comisión ya incluye el IVA.</summary>
    public bool TaxInclusive { get; set; } = true;

    /// <summary>Plantilla de la descripción del concepto.</summary>
    public string ConceptTemplate { get; set; } = "Comisión por recargas en línea";
}
