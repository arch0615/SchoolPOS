using SchoolPOS.Domain.Entities;
using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Domain.Abstractions;

/// <summary>
/// Servicio de tesorería (FR-TRE). Maneja la sesión de caja (apertura con fondo, arqueo al
/// cierre con cálculo de variación) y los movimientos manuales de efectivo. El monto esperado
/// al cierre = fondo inicial + ingresos - egresos + ventas en efectivo de la sesión.
/// </summary>
public interface ITreasuryService
{
    /// <summary>Abre una sesión de caja con fondo inicial (FR-TRE-1). Un operador no puede tener dos abiertas.</summary>
    Task<CashSession> OpenSessionAsync(
        Guid schoolId, Guid operatorId, decimal openingFloat, CancellationToken ct = default);

    /// <summary>Registra un ingreso o egreso manual de efectivo (FR-TRE-2).</summary>
    Task<CashMovement> RegisterMovementAsync(
        Guid sessionId, CashMovementType type, decimal amount, string reason, Guid operatorId,
        CancellationToken ct = default);

    /// <summary>
    /// Cierra la sesión con el monto contado (arqueo): calcula el esperado y la variación
    /// (contado - esperado), FR-TRE-1/3.
    /// </summary>
    Task<CashSession> CloseSessionAsync(Guid sessionId, decimal countedAmount, CancellationToken ct = default);
}
