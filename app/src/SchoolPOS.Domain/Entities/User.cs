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

    public DateTime CreatedAtUtc { get; set; }
}
