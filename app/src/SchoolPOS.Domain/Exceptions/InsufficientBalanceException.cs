namespace SchoolPOS.Domain.Exceptions;

/// <summary>
/// Se lanza cuando una venta/cargo excede el saldo disponible más el límite de sobregiro
/// (política por defecto: bloquear, FR-ACC-3). Garantiza que no haya sobregiro ni doble gasto.
/// </summary>
public class InsufficientBalanceException : Exception
{
    public Guid AccountId { get; }
    public decimal Requested { get; }
    public decimal Available { get; }

    public InsufficientBalanceException(Guid accountId, decimal requested, decimal available)
        : base($"Saldo insuficiente en la cuenta {accountId}: se requiere {requested}, disponible {available}.")
    {
        AccountId = accountId;
        Requested = requested;
        Available = available;
    }
}
