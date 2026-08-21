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
    Guid AccountId);

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
