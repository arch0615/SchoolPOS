using SchoolPOS.Domain.Entities;

namespace SchoolPOS.Domain.Abstractions;

/// <summary>Alumno del padrón, con su saldo, para la pantalla de administración.</summary>
public sealed record StudentRow(
    Guid StudentId,
    Guid AccountId,
    string EnrollmentNo,
    string? CardCode,
    string FullName,
    decimal Balance,
    bool IsActive);

/// <summary>
/// Alta y mantenimiento del padrón de alumnos (FR-ADM-2). Hasta ahora ninguna pantalla del
/// producto creaba alumnos — solo el sembrador de datos de demostración —, de modo que una escuela
/// recién instalada no podía dar de alta a nadie y todo el modelo de saldo quedaba inalcanzable.
/// <para>
/// Crear un alumno crea también su cuenta de saldo: la relación es 1:1 y un alumno sin cuenta no
/// podría comprar.
/// </para>
/// </summary>
public interface IStudentRegistry
{
    /// <summary>Lista el padrón, filtrando por nombre/matrícula/credencial si se indica.</summary>
    Task<IReadOnlyList<StudentRow>> ListAsync(
        Guid schoolId, string? search = null, bool includeInactive = false, CancellationToken ct = default);

    /// <summary>Da de alta al alumno y su cuenta. La matrícula es obligatoria y única por escuela.</summary>
    Task<Student> CreateAsync(
        Guid schoolId, string enrollmentNo, string fullName, string? cardCode,
        CancellationToken ct = default);

    /// <summary>Actualiza los datos del alumno (no toca el saldo).</summary>
    Task UpdateAsync(
        Guid studentId, string enrollmentNo, string fullName, string? cardCode,
        CancellationToken ct = default);

    /// <summary>
    /// Da de baja o reactiva. No se borra: su historial de ventas y movimientos debe seguir
    /// existiendo, así que la baja es lógica.
    /// </summary>
    Task SetActiveAsync(Guid studentId, bool isActive, CancellationToken ct = default);
}
