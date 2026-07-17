namespace SchoolPOS.Domain.Abstractions;

/// <summary>Datos fiscales del receptor del CFDI (la escuela a la que se factura la comisión).</summary>
public sealed record CfdiReceiver(
    string Rfc,
    string Name,
    string TaxRegime,   // RegimenFiscalReceptor, p. ej. "601", "626"
    string PostalCode,  // DomicilioFiscalReceptor (código postal)
    string CfdiUse);    // UsoCFDI, p. ej. "G03" (gastos en general)

/// <summary>
/// Solicitud para emitir el CFDI de comisión a una escuela. El <b>emisor</b> (el proveedor) se
/// configura en la implementación (datos fiscales + CSD en el PAC), no viaja aquí.
/// </summary>
public sealed record CommissionInvoiceRequest(
    CfdiReceiver Receiver,
    decimal Amount,        // comisión total del periodo
    decimal TaxRate,       // IVA aplicable (p. ej. 0.16); 0 si exento
    bool TaxInclusive,     // si Amount ya incluye el IVA
    string Currency,       // "MXN"
    string Concept,        // descripción, p. ej. "Comisión por recargas en línea (periodo…)"
    DateTime PeriodFromUtc,
    DateTime PeriodToUtc);

/// <summary>Resultado del timbrado.</summary>
public sealed record CfdiResult(bool Success, string? Uuid, string? StampedXml, string? Error)
{
    public static CfdiResult Ok(string uuid, string? xml) => new(true, uuid, xml, null);
    public static CfdiResult Fail(string error) => new(false, null, null, error);
}

/// <summary>
/// Emisión de CFDI (factura electrónica) de la comisión a la escuela (FR-COM-5). La implementación
/// real usa el PAC (SW Sapien); en desarrollo se usa un emisor simulado. El emisor real crea
/// documentos fiscales verdaderos: usar solo con credenciales de sandbox hasta validar con contador.
/// </summary>
public interface ICfdiIssuer
{
    Task<CfdiResult> IssueAsync(CommissionInvoiceRequest request, CancellationToken ct = default);
}
