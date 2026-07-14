namespace SchoolPOS.Domain.Entities;

/// <summary>
/// Estudiante. Se identifica por número de matrícula (único por escuela) y, como vía
/// rápida en el POS, por tarjeta con código de barras/QR (FR-SAL-2, Q6). 1:1 con Account.
/// </summary>
public class Student
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SchoolId { get; set; }
    public School School { get; set; } = null!;

    /// <summary>Matrícula. Única dentro de la escuela.</summary>
    public string EnrollmentNo { get; set; } = string.Empty;

    /// <summary>Código de barras/QR de la credencial (vía rápida de cobro). Opcional.</summary>
    public string? CardCode { get; set; }

    public string FullName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    /// <summary>Cuenta de saldo 1:1.</summary>
    public Account Account { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; }
}
