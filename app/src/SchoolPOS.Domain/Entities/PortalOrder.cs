using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Domain.Entities;

/// <summary>
/// Pedido anticipado hecho por un tutor desde el portal (menú semanal o catálogo visible). Nace
/// en la nube — a diferencia de una <see cref="Sale"/>, que nace en la caja — y se cobra ahí mismo
/// de inmediato (mismo <see cref="Services.IBalanceService"/> que usa la caja, así que también
/// respeta el presupuesto diario del tutor). Llega a la caja por el mismo camino que una recarga
/// confirmada: la caja lo baja, lo aplica a su propio libro mayor local y lo acusa — así el saldo
/// de la caja (fuente única de verdad) queda igual de correcto que si el alumno lo hubiera pagado
/// ahí mismo.
/// </summary>
public class PortalOrder
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SchoolId { get; set; }

    public Guid StudentId { get; set; }
    public Guid AccountId { get; set; }

    /// <summary>Día para el que se pidió (p. ej. el menú del jueves). Nulo = lo antes posible.</summary>
    public DateTime? RequestedForDate { get; set; }

    public decimal Total { get; set; }

    public PortalOrderStatus Status { get; set; } = PortalOrderStatus.Placed;

    /// <summary>
    /// Ya lo bajó y lo aplicó la caja a su libro mayor local (mismo sentido que
    /// <see cref="TopUp.AppliedLocally"/>) — independiente de <see cref="Status"/>, que solo
    /// describe la entrega física del pedido, no si ya se descontó el saldo.
    /// </summary>
    public bool AppliedLocally { get; set; }

    public ICollection<PortalOrderLine> Lines { get; set; } = new List<PortalOrderLine>();

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? AppliedAtUtc { get; set; }
    public DateTime? FulfilledAtUtc { get; set; }
}
