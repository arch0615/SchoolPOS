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

/// <summary>
/// Reportes de ventas (FR-SAL-6): por periodo, producto, cajero y método de cobro
/// (saldo/efectivo). Datos para exhibir y exportar.
/// </summary>
public interface ISalesReportService
{
    Task<SalesSummary> GetSummaryAsync(Guid schoolId, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default);

    Task<IReadOnlyList<ProductSalesRow>> GetByProductAsync(
        Guid schoolId, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default);

    Task<IReadOnlyList<CashierSalesRow>> GetByCashierAsync(
        Guid schoolId, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default);
}
