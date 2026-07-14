using Microsoft.EntityFrameworkCore;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Data.Services;

/// <summary>Reportes financieros. Agrega en memoria (portable entre proveedores).</summary>
public sealed class FinancialReportService : IFinancialReportService
{
    private readonly SchoolDbContext _db;

    public FinancialReportService(SchoolDbContext db) => _db = db;

    public async Task<CashFlowSummary> GetCashFlowAsync(
        Guid schoolId, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default)
    {
        // Ventas en efectivo del periodo.
        var salesQuery = _db.Sales.AsNoTracking()
            .Where(s => s.SchoolId == schoolId && s.Tender == TenderType.Cash);
        if (fromUtc is { } sf) salesQuery = salesQuery.Where(s => s.CreatedAtUtc >= sf);
        if (toUtc is { } st) salesQuery = salesQuery.Where(s => s.CreatedAtUtc <= st);
        var cashSales = (await salesQuery.Select(s => s.Total).ToListAsync(ct)).Sum();

        // Movimientos manuales de efectivo del periodo (por sesión de caja de la escuela).
        var movementsQuery =
            from m in _db.CashMovements.AsNoTracking()
            join session in _db.CashSessions.AsNoTracking() on m.CashSessionId equals session.Id
            where session.SchoolId == schoolId
            select new { m.Type, m.Amount, m.CreatedAtUtc };
        if (fromUtc is { } mf) movementsQuery = movementsQuery.Where(m => m.CreatedAtUtc >= mf);
        if (toUtc is { } mt) movementsQuery = movementsQuery.Where(m => m.CreatedAtUtc <= mt);
        var movements = await movementsQuery.Select(m => new { m.Type, m.Amount }).ToListAsync(ct);

        var income = movements.Where(m => m.Type == CashMovementType.Income).Sum(m => m.Amount);
        var expense = movements.Where(m => m.Type == CashMovementType.Expense).Sum(m => m.Amount);

        return new CashFlowSummary(
            Round(cashSales), Round(income), Round(expense),
            Round(cashSales + income - expense));
    }

    public async Task<CustomerBalancesSummary> GetCustomerBalancesAsync(
        Guid schoolId, CancellationToken ct = default)
    {
        var balances = await
            (from a in _db.Accounts.AsNoTracking()
             join s in _db.Students.AsNoTracking() on a.StudentId equals s.Id
             where s.SchoolId == schoolId
             select a.Balance)
            .ToListAsync(ct);

        return new CustomerBalancesSummary(balances.Count, Round(balances.Sum()));
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
