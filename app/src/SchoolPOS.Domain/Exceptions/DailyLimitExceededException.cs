namespace SchoolPOS.Domain.Exceptions;

/// <summary>
/// Se lanza cuando una venta excedería el presupuesto diario que el tutor fijó para el alumno
/// (<see cref="Entities.Account.DailySpendLimit"/>), aunque el saldo alcance. Es un límite del
/// tutor, no de saldo — se evalúa aparte del sobregiro (<see cref="InsufficientBalanceException"/>).
/// </summary>
public class DailyLimitExceededException : Exception
{
    public Guid AccountId { get; }
    public decimal Requested { get; }
    public decimal SpentToday { get; }
    public decimal DailyLimit { get; }

    public DailyLimitExceededException(Guid accountId, decimal requested, decimal spentToday, decimal dailyLimit)
        : base($"La cuenta {accountId} excedería su presupuesto diario: ya gastó {spentToday} de {dailyLimit}, " +
               $"esta venta requiere {requested} más.")
    {
        AccountId = accountId;
        Requested = requested;
        SpentToday = spentToday;
        DailyLimit = dailyLimit;
    }
}
