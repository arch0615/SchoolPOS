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

        var rows = await query
            .OrderByDescending(a => a.CreatedAtUtc)
            .Take(500)
            .Select(a => new AuditEntryRow(
                a.CreatedAtUtc, a.Actor, a.Action, a.Entity, a.EntityId, a.Before, a.After))
            .ToListAsync(ct);

        // El actor se guarda como el Id del operador. Mostrarlo así deja una bitácora ilegible
        // ("quién hizo esto" es justamente su razón de ser), de modo que se resuelve al usuario.
        // Se hace en memoria y sobre lo ya paginado: son 500 filas como máximo.
        var operators = await _db.Users.AsNoTracking()
            .Where(u => u.SchoolId == schoolId)
            .Select(u => new { Id = u.Id.ToString(), u.Username })
            .ToDictionaryAsync(u => u.Id, u => u.Username, ct);

        return rows
            .Select(r => operators.TryGetValue(r.Actor ?? string.Empty, out var name)
                ? r with { Actor = name }
                : r)
            .ToList();
    }
}
