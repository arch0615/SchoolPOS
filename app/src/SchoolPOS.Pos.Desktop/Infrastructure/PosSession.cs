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

    /// <summary>
    /// Devolver una venta (FR-SAL-5). Restringido al administrador, igual que los descuentos: es
    /// dinero saliendo, y quien cobra no debería poder revertir su propio cobro sin supervisión.
    /// </summary>
    public bool CanRefund => Role is UserRole.Admin;
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

    /// <summary>
    /// Padrón de alumnos: alta y baja de inscritos. Almacén también, porque en la práctica quien
    /// atiende el mostrador es quien detecta que falta inscribir a un alumno.
    /// </summary>
    public bool CanManageStudents => Role is UserRole.Warehouse or UserRole.Admin;

    /// <summary>Alta y baja de operadores: solo el administrador reparte accesos.</summary>
    public bool CanManageOperators => Role is UserRole.Admin;

    /// <summary>Configuración de la escuela: función administrativa.</summary>
    public bool CanManageSettings => Role is UserRole.Admin;

    /// <summary>
    /// Ajuste manual de saldo (FR-ADM-2), para corregir una recarga o cargo mal aplicado sin pasar
    /// por una venta/devolución que no encaja con el error real. Administrador solamente: mueve
    /// dinero fuera del flujo normal de venta/recarga, igual que <see cref="CanRefund"/>.
    /// </summary>
    public bool CanAdjustBalance => Role is UserRole.Admin;

    public void SignIn(User user) => Operator = user;

    public void SignOut() => Operator = null;
}
