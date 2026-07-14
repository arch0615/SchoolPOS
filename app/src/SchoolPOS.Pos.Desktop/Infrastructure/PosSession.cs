using SchoolPOS.Domain.Entities;
using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Pos.Desktop.Infrastructure;

/// <summary>
/// Estado de la sesión del operador conectado. Singleton en la app: guarda la escuela (de la
/// configuración local) y el operador tras el login, para control de rol en toda la UI.
/// </summary>
public sealed class PosSession
{
    public Guid SchoolId { get; set; }

    public User? Operator { get; private set; }

    public bool IsAuthenticated => Operator is not null;

    public UserRole Role => Operator?.Role ?? UserRole.Cashier;

    /// <summary>Permisos derivados del rol (control de acceso, FR-POS-1).</summary>
    public bool CanSell => IsAuthenticated;
    public bool CanManageInventory => Role is UserRole.Warehouse or UserRole.Admin;
    public bool CanApplyDiscount => Role is UserRole.Admin;
    public bool IsAdmin => Role is UserRole.Admin;

    public void SignIn(User user) => Operator = user;

    public void SignOut() => Operator = null;
}
