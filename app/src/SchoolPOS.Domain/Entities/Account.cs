namespace SchoolPOS.Domain.Entities;

/// <summary>
/// Cuenta de saldo del estudiante. Es la <b>fuente única de verdad</b> del saldo en la
/// DB local (NFR-1). El portal la <i>incrementa</i> (recargas) y el POS la <i>consume</i>
/// (ventas). Todo cambio de <see cref="Balance"/> se realiza dentro de una transacción
/// atómica junto con su asiento en <see cref="BalanceMovement"/>.
/// </summary>
public class Account
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid StudentId { get; set; }
    public Student Student { get; set; } = null!;

    /// <summary>Saldo disponible. Nunca se modifica sin registrar el movimiento correspondiente.</summary>
    public decimal Balance { get; set; }

    /// <summary>
    /// Límite de sobregiro permitido (default 0 = bloquea venta sin saldo suficiente, FR-ACC-3).
    /// El saldo puede bajar hasta -OverdraftLimit.
    /// </summary>
    public decimal OverdraftLimit { get; set; } = 0m;

    /// <summary>Asientos inmutables del libro mayor de esta cuenta.</summary>
    public ICollection<BalanceMovement> Movements { get; set; } = new List<BalanceMovement>();

    public DateTime UpdatedAtUtc { get; set; }
}
