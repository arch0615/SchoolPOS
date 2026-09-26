using SchoolPOS.Domain.Entities;
using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Domain.Abstractions;

/// <summary>Un artículo del catálogo visible en el portal (venta directa o menú del día).</summary>
public sealed record CatalogItem(
    Guid ProductId, string Name, decimal Price, DayOfWeek? MenuDayOfWeek);

/// <summary>Renglón pedido al colocar un pedido anticipado.</summary>
public sealed record PortalOrderLineRequest(Guid ProductId, decimal Quantity);

/// <summary>Un renglón de pedido, para mostrarlo (portal del tutor o de la escuela).</summary>
public sealed record PortalOrderLineRow(string Description, decimal Quantity, decimal UnitPrice, decimal LineTotal);

/// <summary>Un pedido, con sus renglones, para listarlo.</summary>
public sealed record PortalOrderRow(
    Guid Id, string StudentName, DateTime? RequestedForDate, decimal Total,
    PortalOrderStatus Status, DateTime CreatedAtUtc, IReadOnlyList<PortalOrderLineRow> Lines);

/// <summary>
/// Pedidos anticipados desde el portal (menú semanal y catálogo visible, FR-WP). El pedido se
/// cobra de inmediato en la nube (mismo <see cref="IBalanceService"/> que usa la caja) y llega a
/// la caja por el mismo camino que una recarga confirmada — ver <see cref="PortalOrder"/>.
/// </summary>
public interface IPortalOrderService
{
    /// <summary>Catálogo visible para los tutores de esta escuela: productos con ShowInPortal, más el menú de hoy.</summary>
    Task<IReadOnlyList<CatalogItem>> GetCatalogAsync(Guid schoolId, CancellationToken ct = default);

    /// <summary>
    /// Coloca el pedido y cobra de inmediato (respeta saldo, sobregiro y presupuesto diario —
    /// las mismas reglas que una venta en caja). Verifica que <paramref name="accountId"/>
    /// pertenezca al tutor, igual que el resto de las operaciones del portal.
    /// </summary>
    Task<PortalOrder> PlaceOrderAsync(
        Guid guardianId, Guid accountId, IReadOnlyList<PortalOrderLineRequest> lines,
        DateTime? requestedForDate, CancellationToken ct = default);

    /// <summary>Pedidos del tutor (todos sus alumnos), más recientes primero.</summary>
    Task<IReadOnlyList<PortalOrderRow>> GetOrdersForGuardianAsync(Guid guardianId, CancellationToken ct = default);

    /// <summary>
    /// Cancela un pedido y reintegra su cargo — solo permitido mientras la caja todavía no lo haya
    /// bajado (<c>!AppliedLocally</c>): una vez que la caja ya lo aplicó a su libro mayor local,
    /// cancelarlo aquí dejaría los dos saldos desincronizados sin una reversión real en la caja.
    /// </summary>
    Task CancelOrderAsync(Guid guardianId, Guid orderId, CancellationToken ct = default);

    /// <summary>Pedidos pendientes de entregar para la escuela (para la pantalla de la escuela en el portal).</summary>
    Task<IReadOnlyList<PortalOrderRow>> GetPendingOrdersForSchoolAsync(Guid schoolId, CancellationToken ct = default);

    /// <summary>Marca un pedido como entregado. Solo del lado de la nube — no requiere sincronizar nada a la caja.</summary>
    Task MarkFulfilledAsync(Guid schoolId, Guid orderId, CancellationToken ct = default);
}
