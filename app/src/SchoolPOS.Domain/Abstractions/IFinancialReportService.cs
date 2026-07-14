namespace SchoolPOS.Domain.Abstractions;

/// <summary>Resumen de flujo de efectivo del periodo (FR-TRE-4).</summary>
public sealed record CashFlowSummary(
    decimal CashSales,
    decimal ManualIncome,
    decimal ManualExpense,
    decimal Net);

/// <summary>Estado de saldos de clientes (pasivo por saldo a favor de estudiantes).</summary>
public sealed record CustomerBalancesSummary(int AccountCount, decimal TotalBalance);

/// <summary>
/// Reportes financieros básicos (FR-TRE-4): flujo de efectivo (ventas en efectivo, ingresos y
/// egresos manuales) y estado de saldos de clientes.
/// </summary>
public interface IFinancialReportService
{
    Task<CashFlowSummary> GetCashFlowAsync(
        Guid schoolId, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default);

    Task<CustomerBalancesSummary> GetCustomerBalancesAsync(Guid schoolId, CancellationToken ct = default);
}
