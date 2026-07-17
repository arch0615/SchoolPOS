using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Domain.Entities;

/// <summary>
/// Registro de la factura de comisión (CFDI) emitida a una escuela por un periodo (FR-COM-5).
/// Representa dinero/obligación fiscal: se conserva el folio fiscal (UUID) y el XML timbrado.
/// </summary>
public class CommissionInvoice
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SchoolId { get; set; }

    public DateTime PeriodFromUtc { get; set; }
    public DateTime PeriodToUtc { get; set; }

    /// <summary>Comisión facturada (total del periodo).</summary>
    public decimal CommissionAmount { get; set; }

    public string Currency { get; set; } = "MXN";

    public CfdiStatus Status { get; set; } = CfdiStatus.Draft;

    /// <summary>Folio fiscal (UUID) devuelto por el PAC al timbrar.</summary>
    public string? Uuid { get; set; }

    /// <summary>XML timbrado (comprobante fiscal).</summary>
    public string? StampedXml { get; set; }

    /// <summary>Mensaje de error si el timbrado falló.</summary>
    public string? Error { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? StampedAtUtc { get; set; }
}
