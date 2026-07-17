namespace SchoolPOS.Domain.Enums;

/// <summary>Tipo de movimiento de inventario (Kardex).</summary>
public enum StockMovementType
{
    /// <summary>Entrada de mercancía (compra/recepción). Suma stock.</summary>
    Entry = 1,
    /// <summary>Salida (venta, merma, consumo interno). Resta stock.</summary>
    Exit = 2,
    /// <summary>Ajuste por conteo físico (puede sumar o restar).</summary>
    Adjustment = 3,
}

/// <summary>Estado del ciclo de vida de una orden de compra.</summary>
public enum PurchaseOrderStatus
{
    Draft = 1,
    Sent = 2,
    PartiallyReceived = 3,
    Received = 4,
    Cancelled = 5,
}

/// <summary>Estado de pago de una factura de proveedor.</summary>
public enum SupplierInvoiceStatus
{
    Pending = 1,
    PartiallyPaid = 2,
    Paid = 3,
}

/// <summary>Estado de la sesión de caja (arqueo).</summary>
public enum CashSessionStatus
{
    Open = 1,
    Closed = 2,
}

/// <summary>Movimiento manual de efectivo no derivado de ventas.</summary>
public enum CashMovementType
{
    Income = 1,
    Expense = 2,
}

/// <summary>Estado de una venta.</summary>
public enum SaleStatus
{
    Completed = 1,
    PartiallyRefunded = 2,
    Refunded = 3,
}

/// <summary>Estado de una factura de comisión (CFDI) emitida a la escuela.</summary>
public enum CfdiStatus
{
    /// <summary>Registrada, aún no timbrada.</summary>
    Draft = 1,
    /// <summary>Timbrada por el PAC (tiene folio fiscal / UUID).</summary>
    Stamped = 2,
    /// <summary>Falló el timbrado.</summary>
    Failed = 3,
    /// <summary>Cancelada ante el SAT.</summary>
    Cancelled = 4,
}
