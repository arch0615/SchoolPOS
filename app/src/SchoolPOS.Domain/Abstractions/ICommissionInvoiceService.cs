using SchoolPOS.Domain.Entities;

namespace SchoolPOS.Domain.Abstractions;

/// <summary>
/// Orquesta la facturación de la comisión a una escuela por un periodo (FR-COM-5): calcula la
/// comisión capturada, arma el CFDI con los datos fiscales de la escuela, lo timbra vía
/// <see cref="ICfdiIssuer"/> y persiste el registro <see cref="CommissionInvoice"/>.
/// </summary>
public interface ICommissionInvoiceService
{
    /// <summary>
    /// Emite (o reintenta) la factura de comisión de la escuela para el periodo indicado. Lanza si
    /// faltan datos fiscales de la escuela o si no hay comisión que facturar.
    /// </summary>
    Task<CommissionInvoice> IssueForPeriodAsync(
        Guid schoolId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
}
