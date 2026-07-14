namespace SchoolPOS.Domain.Abstractions;

/// <summary>Estudiante localizado en el POS junto con su saldo disponible.</summary>
public sealed record StudentBalance(
    Guid StudentId, Guid AccountId, string EnrollmentNo, string FullName, decimal Balance);

/// <summary>
/// Identificación del cliente en el POS (FR-SAL-2, Q6): localizar al estudiante por número de
/// matrícula o por el código de barras/QR de su credencial, para cobrar contra su saldo.
/// </summary>
public interface IStudentDirectory
{
    /// <summary>
    /// Busca por matrícula o por código de credencial (barcode/QR). Devuelve el estudiante activo
    /// con su cuenta y saldo, o <c>null</c> si no existe.
    /// </summary>
    Task<StudentBalance?> FindByCodeAsync(Guid schoolId, string code, CancellationToken ct = default);
}
