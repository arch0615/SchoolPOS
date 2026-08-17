using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Domain.Entities;

/// <summary>Operador interno del POS (cajero/almacén/administrador) con control de rol (FR-ADM-1).</summary>
public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SchoolId { get; set; }

    public string Username { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public UserRole Role { get; set; } = UserRole.Cashier;

    public bool IsActive { get; set; } = true;

    /// <summary>Intentos fallidos consecutivos de inicio de sesión (se limpia al entrar bien).</summary>
    public int FailedLoginCount { get; set; }

    /// <summary>Si tiene valor futuro, la cuenta está bloqueada hasta esa fecha (NFR-6).</summary>
    public DateTime? LockedUntilUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
