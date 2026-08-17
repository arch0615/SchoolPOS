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
    public bool CanViewReports => Role is UserRole.Admin;

    /// <summary>Compras: alimenta el inventario (recepción de mercancía), mismo permiso que Inventario.</summary>
    public bool CanManagePurchasing => Role is UserRole.Warehouse or UserRole.Admin;

    /// <summary>
    /// Abrir y cerrar su <b>propia</b> caja. Lo puede hacer cualquier operador que cobre en
    /// efectivo: si solo el administrador pudiera abrirla, un cajero sin él no podría vender en
    /// efectivo — y las ventas quedarían fuera del arqueo, que es justo lo que se quiere evitar.
    /// </summary>
    public bool CanOperateOwnTill => IsAuthenticated;

    /// <summary>Ver el histórico de arqueos de toda la escuela: función administrativa.</summary>
    public bool CanViewAllTillSessions => Role is UserRole.Admin;

    /// <summary>Configuración de la escuela: función administrativa.</summary>
    public bool CanManageSettings => Role is UserRole.Admin;

    public void SignIn(User user) => Operator = user;

    public void SignOut() => Operator = null;
}
