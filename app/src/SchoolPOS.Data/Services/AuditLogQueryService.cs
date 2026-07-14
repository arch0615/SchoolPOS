using Microsoft.EntityFrameworkCore;
using SchoolPOS.Domain.Abstractions;

namespace SchoolPOS.Data.Services;

/// <summary>Implementación del visor de bitácora (solo lectura).</summary>
public sealed class AuditLogQueryService : IAuditLogQueryService
{
    private readonly SchoolDbContext _db;

    public AuditLogQueryService(SchoolDbContext db) => _db = db;

    public async Task<IReadOnlyList<AuditEntryRow>> QueryAsync(
        Guid schoolId, DateTime? fromUtc, DateTime? toUtc, string? action, CancellationToken ct = default)
    {
        var query = _db.AuditLogs.AsNoTracking().Where(a => a.SchoolId == schoolId);
        if (fromUtc is { } from) query = query.Where(a => a.CreatedAtUtc >= from);
        if (toUtc is { } to) query = query.Where(a => a.CreatedAtUtc <= to);
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(a => a.Action == action);

        return await query
            .OrderByDescending(a => a.CreatedAtUtc)
            .Take(500)
            .Select(a => new AuditEntryRow(
                a.CreatedAtUtc, a.Actor, a.Action, a.Entity, a.EntityId, a.Before, a.After))
            .ToListAsync(ct);
    }
}
