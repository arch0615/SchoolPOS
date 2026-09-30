using Microsoft.EntityFrameworkCore;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Entities;
using SchoolPOS.Domain.Enums;
using SchoolPOS.Domain.Exceptions;

namespace SchoolPOS.Data.Services;

/// <inheritdoc cref="IPortalOrderService"/>
public sealed class PortalOrderService : IPortalOrderService
{
    private readonly SchoolDbContext _db;
    private readonly IBalanceService _balance;
    private readonly IClock _clock;

    private const int Scale = 2;
    private const MidpointRounding Rounding = MidpointRounding.AwayFromZero;

    // Sin operador físico: el pedido nace en el portal, no en una caja con un cajero presente —
    // mismo motivo por el que BalanceMovement.OperatorId ya es nulo para recargas del portal.
    // ChargeSaleAsync/RefundAsync exigen un Guid no nulo (pensados para el flujo normal de caja),
    // así que se usa este centinela documentado en vez de tocar esa firma para todos sus llamadores.
    private static readonly Guid PortalOperator = Guid.Empty;

    public PortalOrderService(SchoolDbContext db, IBalanceService balance, IClock clock)
    {
        _db = db;
        _balance = balance;
        _clock = clock;
    }

    public async Task<IReadOnlyList<CatalogItem>> GetCatalogAsync(Guid schoolId, CancellationToken ct = default) =>
        await _db.Products.AsNoTracking()
            .Where(p => p.SchoolId == schoolId && p.IsActive && (p.ShowInPortal || p.MenuDayOfWeek != null))
            .OrderBy(p => p.MenuDayOfWeek.HasValue ? 0 : 1).ThenBy(p => p.Name)
            .Select(p => new CatalogItem(p.Id, p.Name, p.Price, p.MenuDayOfWeek))
            .ToListAsync(ct);

    public async Task<PortalOrder> PlaceOrderAsync(
        Guid guardianId, Guid accountId, IReadOnlyList<PortalOrderLineRequest> lines,
        DateTime? requestedForDate, CancellationToken ct = default)
    {
        if (lines.Count == 0)
            throw new ArgumentException("El pedido no tiene artículos.", nameof(lines));

        return await _db.ExecuteAtomicAsync(async () =>
        {
            var owned = await (
                from gs in _db.GuardianStudents.AsNoTracking()
                join a in _db.Accounts.AsNoTracking() on gs.StudentId equals a.StudentId
                where gs.GuardianId == guardianId && a.Id == accountId
                select new { a.Id, StudentId = gs.StudentId, a.Student.SchoolId }).FirstOrDefaultAsync(ct)
                ?? throw new InvalidOperationException("Ese alumno no está vinculado a tu cuenta.");

            var productIds = lines.Select(l => l.ProductId).Distinct().ToList();
            var products = await _db.Products.AsNoTracking()
                .Where(p => productIds.Contains(p.Id) && p.SchoolId == owned.SchoolId && p.IsActive
                            && (p.ShowInPortal || p.MenuDayOfWeek != null))
                .ToDictionaryAsync(p => p.Id, ct);

            // Existencia por producto (no por renglón): dos renglones del mismo artículo deben
            // validarse contra el total pedido, no cada uno por separado.
            var requestedByProduct = lines.GroupBy(l => l.ProductId).ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
            foreach (var (productId, requested) in requestedByProduct)
            {
                if (!products.TryGetValue(productId, out var product))
                    continue; // se reporta abajo, al construir los renglones
                // Existencia sincronizada de la última corrida de la caja, no en tiempo real (ver
                // ProductEntryDto.StockOnHand) — rechaza lo claramente imposible, no es una reserva
                // exacta bajo pedidos simultáneos.
                if (requested > product.StockOnHand)
                    throw new InsufficientStockException(productId, requested, product.StockOnHand);
            }

            var orderLines = new List<PortalOrderLine>();
            foreach (var l in lines)
            {
                if (l.Quantity <= 0m)
                    throw new ArgumentException("La cantidad debe ser positiva.", nameof(lines));
                if (!products.TryGetValue(l.ProductId, out var product))
                    throw new InvalidOperationException($"El artículo {l.ProductId} no está disponible en el portal.");

                var lineTotal = Round(l.Quantity * product.Price);
                orderLines.Add(new PortalOrderLine
                {
                    ProductId = product.Id,
                    Description = product.Name,
                    Quantity = l.Quantity,
                    UnitPrice = product.Price,
                    LineTotal = lineTotal,
                });
            }

            var total = Round(orderLines.Sum(l => l.LineTotal));
            var order = new PortalOrder
            {
                SchoolId = owned.SchoolId,
                StudentId = owned.StudentId,
                AccountId = accountId,
                RequestedForDate = requestedForDate?.Date,
                Total = total,
                Lines = orderLines,
                CreatedAtUtc = _clock.UtcNow,
            };
            _db.PortalOrders.Add(order);
            await _db.SaveChangesAsync(ct);

            // Cobra de inmediato en la nube: mismas reglas que una venta en caja (saldo, sobregiro,
            // presupuesto diario). Si no alcanza, se revierte todo el pedido (misma transacción).
            await _balance.ChargeSaleAsync(accountId, total, order.Id.ToString(), PortalOperator, ct);

            return order;
        }, ct);
    }

    public async Task<IReadOnlyList<PortalOrderRow>> GetOrdersForGuardianAsync(
        Guid guardianId, CancellationToken ct = default)
    {
        var orders = await (
            from gs in _db.GuardianStudents.AsNoTracking()
            join o in _db.PortalOrders.AsNoTracking() on gs.StudentId equals o.StudentId
            join s in _db.Students.AsNoTracking() on o.StudentId equals s.Id
            where gs.GuardianId == guardianId
            orderby o.CreatedAtUtc descending
            select new { o, s.FullName })
            .ToListAsync(ct);

        return await ToRowsAsync(orders.Select(x => (x.o, x.FullName)), ct);
    }

    public async Task CancelOrderAsync(Guid guardianId, Guid orderId, CancellationToken ct = default)
    {
        await _db.ExecuteAtomicAsync(async () =>
        {
            // Verificación de dueño aparte, sin mezclar tracking: unir _db.PortalOrders (con
            // seguimiento, porque se va a modificar) con un join .AsNoTracking() deja la entidad
            // proyectada SIN seguimiento — el cambio de Status de abajo se perdía en silencio.
            var owns = await (
                from o in _db.PortalOrders.AsNoTracking()
                join gs in _db.GuardianStudents.AsNoTracking() on o.StudentId equals gs.StudentId
                where o.Id == orderId && gs.GuardianId == guardianId
                select o.Id).AnyAsync(ct);
            if (!owns)
                throw new InvalidOperationException("Pedido no encontrado.");

            var order = await _db.PortalOrders.FirstOrDefaultAsync(o => o.Id == orderId, ct)
                ?? throw new InvalidOperationException("Pedido no encontrado.");

            if (order.Status != PortalOrderStatus.Placed)
                throw new InvalidOperationException("Ese pedido ya no se puede cancelar.");
            if (order.AppliedLocally)
                throw new InvalidOperationException(
                    "La caja ya recibió este pedido; pide a la escuela que lo cancele ahí.");

            order.Status = PortalOrderStatus.Cancelled;
            await _db.SaveChangesAsync(ct);
            await _balance.RefundAsync(order.AccountId, order.Total, order.Id.ToString(), PortalOperator, ct);
            return true;
        }, ct);
    }

    public async Task<IReadOnlyList<PortalOrderRow>> GetPendingOrdersForSchoolAsync(
        Guid schoolId, CancellationToken ct = default)
    {
        var orders = await (
            from o in _db.PortalOrders.AsNoTracking()
            join s in _db.Students.AsNoTracking() on o.StudentId equals s.Id
            where o.SchoolId == schoolId && o.Status == PortalOrderStatus.Placed
            orderby o.CreatedAtUtc
            select new { o, s.FullName })
            .ToListAsync(ct);

        return await ToRowsAsync(orders.Select(x => (x.o, x.FullName)), ct);
    }

    public async Task MarkFulfilledAsync(Guid schoolId, Guid orderId, CancellationToken ct = default)
    {
        var order = await _db.PortalOrders.FirstOrDefaultAsync(o => o.Id == orderId && o.SchoolId == schoolId, ct)
            ?? throw new InvalidOperationException("Pedido no encontrado.");
        if (order.Status != PortalOrderStatus.Placed)
            throw new InvalidOperationException("Ese pedido ya no está pendiente.");

        order.Status = PortalOrderStatus.Fulfilled;
        order.FulfilledAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private async Task<IReadOnlyList<PortalOrderRow>> ToRowsAsync(
        IEnumerable<(PortalOrder Order, string StudentName)> orders, CancellationToken ct)
    {
        var list = orders.ToList();
        var orderIds = list.Select(x => x.Order.Id).ToList();
        var lines = await _db.PortalOrderLines.AsNoTracking()
            .Where(l => orderIds.Contains(l.OrderId))
            .ToListAsync(ct);
        var byOrder = lines.GroupBy(l => l.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        return list.Select(x => new PortalOrderRow(
            x.Order.Id, x.StudentName, x.Order.RequestedForDate, x.Order.Total, x.Order.Status, x.Order.CreatedAtUtc,
            byOrder.GetValueOrDefault(x.Order.Id, new List<PortalOrderLine>())
                .Select(l => new PortalOrderLineRow(l.Description, l.Quantity, l.UnitPrice, l.LineTotal))
                .ToList()))
            .ToList();
    }

    private static decimal Round(decimal value) => Math.Round(value, Scale, Rounding);
}
