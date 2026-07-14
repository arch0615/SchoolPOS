namespace SchoolPOS.Domain.Entities;

/// <summary>
/// Padre/tutor del portal. Puede administrar varios estudiantes vinculados (FR-WP-9) mediante
/// <see cref="GuardianStudent"/>. Se autentica en el portal (nube) con bloqueo por intentos
/// fallidos (FR-WP-3). Nunca se almacena la contraseña en claro (NFR-6).
/// </summary>
public class Guardian
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SchoolId { get; set; }

    public string Email { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    /// <summary>Hash de contraseña (nunca en claro).</summary>
    public string PasswordHash { get; set; } = string.Empty;

    public int FailedLoginCount { get; set; }
    public DateTime? LockedUntilUtc { get; set; }

    /// <summary>Hash del token de recuperación de contraseña (nunca en claro). De un solo uso.</summary>
    public string? PasswordResetTokenHash { get; set; }
    public DateTime? PasswordResetExpiresUtc { get; set; }

    public ICollection<GuardianStudent> Students { get; set; } = new List<GuardianStudent>();

    public DateTime CreatedAtUtc { get; set; }
}
