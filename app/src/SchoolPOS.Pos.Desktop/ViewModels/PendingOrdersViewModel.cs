using System.Collections.ObjectModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolPOS.Data;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Common;
using SchoolPOS.Domain.Enums;
using SchoolPOS.Pos.Desktop.Infrastructure;

namespace SchoolPOS.Pos.Desktop.ViewModels;

/// <summary>
/// Pedidos anticipados del portal pendientes de entregar (FR-WP): el tutor ya los pagó desde la
/// web, la caja ya los bajó a su libro mayor local (ver <c>SyncAgent.PullOrdersAsync</c>) — esta
/// pantalla es donde, al llegar el alumno, se busca su pedido y se marca entregado. Ese estado
/// sube después a la nube (<c>SyncAgent.PushFulfilledOrdersAsync</c>), el mismo lugar donde
/// "Marcar entregado" del portal web deja el suyo.
/// </summary>
public sealed class PendingOrdersViewModel : ViewModelBase, IAsyncLoadable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PosSession _session;
    private readonly IClock _clock;

    private string _search = string.Empty;
    private PendingOrderRow? _selectedOrder;
    private string _statusMessage = string.Empty;
    private string _errorMessage = string.Empty;

    public PendingOrdersViewModel(IServiceScopeFactory scopeFactory, PosSession session, IClock clock)
    {
        _scopeFactory = scopeFactory;
        _session = session;
        _clock = clock;

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        DeliverCommand = new AsyncRelayCommand(DeliverAsync, () => SelectedOrder is not null);
    }

    public ObservableCollection<PendingOrderRow> Orders { get; } = new();

    /// <summary>Filtro por alumno (nombre o matrícula) — un lector de código de barras basta con enfocar este campo, escanear y Enter/Buscar.</summary>
    public string Search
    {
        get => _search;
        set => SetProperty(ref _search, value);
    }

    public PendingOrderRow? SelectedOrder
    {
        get => _selectedOrder;
        set { if (SetProperty(ref _selectedOrder, value)) DeliverCommand.RaiseCanExecuteChanged(); }
    }

    public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }
    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand DeliverCommand { get; }

    public async Task LoadAsync()
    {
        ErrorMessage = string.Empty;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();

            // En minúsculas: SQLite distingue mayúsculas en Contains() y SQL Server no (mismo
            // motivo documentado en Devoluciones).
            var term = Search.Trim().ToLower();

            var query =
                from o in db.PortalOrders.AsNoTracking()
                where o.SchoolId == _session.SchoolId && o.Status == PortalOrderStatus.Placed
                join st in db.Students.AsNoTracking() on o.StudentId equals st.Id
                where term == "" || st.FullName.ToLower().Contains(term) || st.EnrollmentNo.ToLower().Contains(term)
                orderby o.CreatedAtUtc
                select new { o.Id, o.RequestedForDate, o.Total, o.CreatedAtUtc, st.FullName, st.EnrollmentNo };

            var rows = await query.ToListAsync();
            var orderIds = rows.Select(r => r.Id).ToList();
            var lines = await db.PortalOrderLines.AsNoTracking()
                .Where(l => orderIds.Contains(l.OrderId))
                .ToListAsync();
            var byOrder = lines.GroupBy(l => l.OrderId).ToDictionary(g => g.Key, g => g.ToList());

            var selectedId = SelectedOrder?.Id;
            Orders.Clear();
            foreach (var r in rows)
            {
                var items = byOrder.GetValueOrDefault(r.Id, new List<Domain.Entities.PortalOrderLine>())
                    .Select(l => $"{l.Quantity:0.##} × {l.Description}");
                Orders.Add(new PendingOrderRow(
                    r.Id, r.FullName, r.EnrollmentNo, MxTime.Local(r.CreatedAtUtc), r.RequestedForDate, r.Total,
                    string.Join(", ", items)));
            }
            SelectedOrder = Orders.FirstOrDefault(o => o.Id == selectedId);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"No se pudieron cargar los pedidos: {ex.Message}";
        }
    }

    private async Task DeliverAsync()
    {
        if (SelectedOrder is null)
            return;

        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();

            var order = await db.PortalOrders.FirstOrDefaultAsync(o => o.Id == SelectedOrder.Id)
                ?? throw new InvalidOperationException("El pedido ya no existe.");
            order.Status = PortalOrderStatus.Fulfilled;
            order.FulfilledAtUtc = _clock.UtcNow;
            await db.SaveChangesAsync();

            StatusMessage = $"Pedido de {SelectedOrder.StudentName} entregado.";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"No se pudo marcar el pedido como entregado: {ex.Message}";
        }
    }
}

/// <summary>Fila de un pedido pendiente de entregar.</summary>
public sealed record PendingOrderRow(
    Guid Id, string StudentName, string EnrollmentNo, DateTime CreatedAtLocal, DateTime? RequestedForDate,
    decimal Total, string ItemsText)
{
    public string RequestedForDateText => RequestedForDate?.ToString("dd/MM/yyyy") ?? "Lo antes posible";
}
