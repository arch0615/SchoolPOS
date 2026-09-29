using Microsoft.EntityFrameworkCore;
using SchoolPOS.Data;

namespace SchoolPOS.Portal.Web.Infrastructure;

/// <summary>
/// Catálogo de escuelas para los selectores de las pantallas públicas (registro, ingreso y
/// recuperación de contraseña), donde todavía no hay sesión de la que sacar la escuela. Una vez
/// autenticado, la escuela se lee del claim <c>school_id</c> de la cookie, nunca de aquí.
/// </summary>
public sealed class SchoolDirectory
{
    private readonly SchoolDbContext _db;

    public SchoolDirectory(SchoolDbContext db) => _db = db;

    /// <summary>Escuela seleccionable en un desplegable.</summary>
    public sealed record Option(Guid Id, string Name);

    /// <summary>Solo escuelas activas: una dada de baja no debe poder recibir tutores nuevos.</summary>
    public async Task<IReadOnlyList<Option>> ListAsync(CancellationToken ct = default) =>
        await _db.Schools.AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.Name)
            .Select(s => new Option(s.Id, s.Name))
            .ToListAsync(ct);

    /// <summary>
    /// Comprueba que la escuela recibida del formulario existe de verdad y sigue activa. Evita que
    /// un POST manipulado cree tutores colgando de un SchoolId inventado o de una escuela ya dada
    /// de baja.
    /// </summary>
    public Task<bool> ExistsAsync(Guid schoolId, CancellationToken ct = default) =>
        schoolId == Guid.Empty
            ? Task.FromResult(false)
            : _db.Schools.AsNoTracking().AnyAsync(s => s.Id == schoolId && s.IsActive, ct);
}
