using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Data.Sync;

/// <summary>
/// Contrato de <c>/api/sync/*</c>. Compartido entre el portal (servidor) y el Sync Agent
/// (cliente) — ambos referencian este proyecto — para que el tipo en el wire no pueda
/// desalinearse silenciosamente entre los dos lados.
/// </summary>
public sealed record PendingTopUpDto(
    Guid Id,
    Guid SchoolId,
    Guid AccountId,
    decimal Amount,
    decimal CommissionRate,
    decimal CommissionAmount,
    string GatewayRef,
    DateTime CreatedAtUtc);

public sealed record AckTopUpsRequest(List<Guid> TopUpIds);

/// <summary>Una fila del padrón local (alumno + su cuenta). Sin SchoolId: el servidor siempre usa el de la llave autenticada, nunca el que mande el cliente.</summary>
public sealed record RosterEntryDto(
    Guid Id,
    string EnrollmentNo,
    string? CardCode,
    string FullName,
    bool IsActive,
    DateTime CreatedAtUtc,
    Guid AccountId,
    string? Grade = null);

public sealed record RosterPushResult(int Pushed);

public sealed record ConsumptionEntryDto(
    Guid Id,
    Guid AccountId,
    MovementType Type,
    decimal Amount,
    decimal BalanceAfter,
    string? Reference,
    Guid? OperatorId,
    DateTime CreatedAtUtc);

/// <summary>Applied: se guardó (o ya estaba) en la nube — el cliente puede marcarlo sincronizado. Skipped: la cuenta aún no existe en la nube (padrón desfasado) — se reintenta en la próxima corrida.</summary>
public sealed record ConsumptionPushResult(List<Guid> Applied, List<Guid> Skipped);

/// <summary>
/// Presupuesto diario que el tutor fijó desde el portal para una cuenta. A diferencia del padrón
/// y el consumo (que nacen en la escuela), este dato nace en la nube y viaja nube→escuela: la caja
/// lo hace cumplir al cobrar. Reconciliación completa cada corrida (igual que el padrón, por lo
/// acotado del número de cuentas), no solo lo "nuevo".
/// </summary>
public sealed record AccountLimitDto(Guid AccountId, decimal? DailySpendLimit);

public sealed record SaleLineEntryDto(
    Guid Id,
    Guid ProductId,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal Discount,
    decimal LineTotal);

/// <summary>
/// Venta con sus renglones, para que el tutor pueda ver qué compró el alumno (no solo el importe
/// del BalanceMovement) y el reporte de ventas de la escuela en el portal tenga con qué mostrarse.
/// Sin SchoolId: el servidor siempre usa el de la llave autenticada, nunca el que mande el cliente.
/// </summary>
public sealed record SaleEntryDto(
    Guid Id,
    Guid? StudentId,
    Guid? AccountId,
    TenderType Tender,
    SaleStatus Status,
    decimal Subtotal,
    decimal DiscountTotal,
    decimal TaxTotal,
    decimal Total,
    decimal? AmountTendered,
    DateTime CreatedAtUtc,
    List<SaleLineEntryDto> Lines);

/// <summary>Applied: se guardó (o ya estaba) en la nube. Skipped: la cuenta aún no existe en la nube (padrón desfasado) — se reintenta en la próxima corrida, igual que PushConsumptionAsync.</summary>
public sealed record SalesPushResult(List<Guid> Applied, List<Guid> Skipped);
