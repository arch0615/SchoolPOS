using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Domain.Entities;

/// <summary>
/// Asiento del libro mayor de saldo. <b>Inmutable</b> (solo inserción; sin updates/deletes,
/// FR-ACC-4 / NFR-5). El importe es <b>con signo</b>: los abonos (TopUp/Refund) son positivos
/// y los cargos (Sale) negativos, de modo que <c>SUM(Amount) == Account.Balance</c> siempre
/// reconcilia (base del hito M1). Un ajuste puede ser positivo o negativo.
/// </summary>
public class BalanceMovement
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AccountId { get; set; }
    public Account Account { get; set; } = null!;

    public MovementType Type { get; set; }

    /// <summary>Importe con signo (+abono / -cargo).</summary>
    public decimal Amount { get; set; }

    /// <summary>Saldo resultante tras aplicar este movimiento (instantánea para auditoría).</summary>
    public decimal BalanceAfter { get; set; }

    /// <summary>
    /// Referencia de origen: id de venta, id de devolución, o <c>gateway_ref</c> de la recarga.
    /// Para recargas se usa además para deduplicar la aplicación idempotente (NFR-7).
    /// </summary>
    public string? Reference { get; set; }

    /// <summary>Operador del POS que originó el movimiento. Nulo para recargas del portal (sin operador).</summary>
    public Guid? OperatorId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
