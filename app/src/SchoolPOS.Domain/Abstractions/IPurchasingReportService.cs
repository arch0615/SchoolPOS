namespace SchoolPOS.Domain.Abstractions;

/// <summary>Compras agregadas por proveedor.</summary>
public sealed record SupplierPurchaseRow(Guid SupplierId, string SupplierName, int OrderCount, decimal Total);

/// <summary>Compras agregadas por producto (según renglones de órdenes).</summary>
public sealed record ProductPurchaseRow(Guid ProductId, decimal Quantity, decimal Total);

/// <summary>Resumen de compras del periodo.</summary>
public sealed record PurchasingSummary(int OrderCount, decimal Total);

/// <summary>
/// Reportes de compras (FR-PUR-5): por proveedor, periodo o producto. Excluye las órdenes
/// canceladas (no representan compra real).
/// </summary>
public interface IPurchasingReportService
{
    Task<PurchasingSummary> GetSummaryAsync(Guid schoolId, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default);

    Task<IReadOnlyList<SupplierPurchaseRow>> GetBySupplierAsync(
        Guid schoolId, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default);

    Task<IReadOnlyList<ProductPurchaseRow>> GetByProductAsync(
        Guid schoolId, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default);
}
