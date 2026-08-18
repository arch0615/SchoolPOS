using SchoolPOS.Domain.Entities;

namespace SchoolPOS.Domain.Abstractions;

/// <summary>
/// Recarga de saldo en efectivo, capturada en el mostrador de la escuela (FR-SAL-8).
/// <para>
/// Es la contraparte local de la recarga en línea: en el modo de una sola caja no hay portal ni
/// pasarela, así que sin esto un alumno recién inscrito se quedaba en $0.00 para siempre y el
/// modelo de saldo prepagado no cerraba.
/// </para>
/// <para>
/// El dinero entra físicamente al cajón, de modo que la recarga <b>exige una caja abierta</b> y se
/// asienta también como ingreso de esa caja. Si no, el arqueo la reportaría como sobrante — el
/// mismo desfase que ya se corrigió para ventas y devoluciones en efectivo. Como efecto útil, un
/// operador que acredite saldo sin cobrar el efectivo deja la caja cuadrando de menos ese día.
/// </para>
/// </summary>
public interface ICashTopUpService
{
    /// <summary>
    /// Acredita <paramref name="amount"/> al saldo del alumno y registra el ingreso en la caja.
    /// Sin comisión: el proveedor no procesa este dinero.
    /// </summary>
    /// <param name="cashSessionId">Caja abierta del operador. Obligatoria.</param>
    Task<TopUp> CreateAsync(
        Guid schoolId, Guid accountId, decimal amount, Guid operatorId, Guid cashSessionId,
        CancellationToken ct = default);
}
