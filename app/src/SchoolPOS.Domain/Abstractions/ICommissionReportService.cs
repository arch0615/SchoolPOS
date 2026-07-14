namespace SchoolPOS.Domain.Abstractions;

/// <summary>Resumen de comisión de una escuela (recargado vs. comisión), FR-COM-4.</summary>
public sealed record SchoolCommissionSummary(
    Guid SchoolId,
    string SchoolName,
    int TopUpCount,
    decimal TotalRecharged,
    decimal TotalCommission);

/// <summary>
/// Consolidado del proveedor: comisión total a través de todas las escuelas con desglose por
/// escuela (FR-COM-3). Vista "de un vistazo" del ingreso del proveedor.
/// </summary>
public sealed record VendorCommissionRollup(
    decimal TotalRecharged,
    decimal TotalCommission,
    int TopUpCount,
    IReadOnlyList<SchoolCommissionSummary> Schools);

/// <summary>
/// Reportes de comisión sobre recargas. Solo considera recargas <b>capturadas</b> (confirmadas o
/// aplicadas): las pendientes o fallidas no representan comisión cobrada. La comisión es el ingreso
/// real del proveedor, por lo que se calcula sobre datos inmutables del registro de recargas.
/// </summary>
public interface ICommissionReportService
{
    /// <summary>Consolidado del proveedor con desglose por escuela, en el rango de fechas dado (FR-COM-3).</summary>
    Task<VendorCommissionRollup> GetVendorRollupAsync(
        DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default);

    /// <summary>Resumen de una escuela (total recargado y comisión), FR-COM-4.</summary>
    Task<SchoolCommissionSummary> GetSchoolSummaryAsync(
        Guid schoolId, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default);
}
