namespace SchoolPOS.Domain.Enums;

/// <summary>Tipo de movimiento del libro mayor de saldo (BalanceMovement).</summary>
public enum MovementType
{
    /// <summary>Recarga en línea (abono). Crédito.</summary>
    TopUp = 1,
    /// <summary>Venta en tienda (cargo). Débito.</summary>
    Sale = 2,
    /// <summary>Devolución (reintegra saldo). Crédito.</summary>
    Refund = 3,
    /// <summary>Ajuste manual auditado (puede ser crédito o débito).</summary>
    Adjustment = 4,
}

/// <summary>Forma de cobro en el POS.</summary>
public enum TenderType
{
    /// <summary>Contra saldo precargado del estudiante.</summary>
    Balance = 1,
    /// <summary>Efectivo en caja.</summary>
    Cash = 2,
}

/// <summary>Rol del operador interno del POS (control de acceso).</summary>
public enum UserRole
{
    Cashier = 1,
    Warehouse = 2,
    Admin = 3,
}

/// <summary>Estado de una recarga en línea.</summary>
public enum TopUpStatus
{
    /// <summary>Creada, esperando confirmación del webhook de la pasarela.</summary>
    Pending = 1,
    /// <summary>Pago confirmado por webhook (server-side). Ya es dinero real.</summary>
    Confirmed = 2,
    /// <summary>Aplicada al libro mayor local (saldo acreditado).</summary>
    Applied = 3,
    /// <summary>Pago fallido/cancelado.</summary>
    Failed = 4,
}
