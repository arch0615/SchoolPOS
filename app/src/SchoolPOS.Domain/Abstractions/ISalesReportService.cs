namespace SchoolPOS.Domain.Abstractions;

/// <summary>Resumen de ventas por periodo, con desglose por método de cobro (FR-SAL-6).</summary>
public sealed record SalesSummary(
    DateTime? FromUtc,
    DateTime? ToUtc,
    int SaleCount,
    decimal Total,
    decimal TotalByBalance,
    decimal TotalByCash);

/// <summary>Ventas agregadas por producto.</summary>
public sealed record ProductSalesRow(Guid ProductId, string Description, decimal Quantity, decimal Revenue);

/// <summary>Ventas agregadas por cajero.</summary>
public sealed record CashierSalesRow(Guid CashierId, int SaleCount, decimal Total);

/// <summary>Ventas agregadas por alumno. Solo incluye ventas ligadas a un alumno identificado.</summary>
public sealed record StudentSalesRow(Guid StudentId, string StudentName, int SaleCount, decimal Total);

/// <summary>
/// Reportes de ventas (FR-SAL-6): por periodo, producto, cajero, alumno y método de cobro
/// (saldo/efectivo). Datos para exhibir y exportar.
/// </summary>
public interface ISalesReportService
{
    Task<SalesSummary> GetSummaryAsync(Guid schoolId, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default);

    Task<IReadOnlyList<ProductSalesRow>> GetByProductAsync(
        Guid schoolId, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default);

    Task<IReadOnlyList<CashierSalesRow>> GetByCashierAsync(
        Guid schoolId, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default);

    /// <summary>
    /// Ventas por alumno (las de mostrador sin alumno identificado no entran, no tienen a quién
    /// atribuirse). Incluye tanto cobro por saldo como en efectivo cuando el alumno fue
    /// identificado.
    /// </summary>
    Task<IReadOnlyList<StudentSalesRow>> GetByStudentAsync(
        Guid schoolId, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default);
}
