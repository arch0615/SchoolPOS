using System.Collections.ObjectModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolPOS.Data;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Pos.Desktop.Infrastructure;

namespace SchoolPOS.Pos.Desktop.ViewModels;

/// <summary>Dashboard administrativo: indicadores del día y alertas de inventario (FR-POS-2).</summary>
public sealed class DashboardViewModel : ViewModelBase, IAsyncLoadable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PosSession _session;
    private readonly IClock _clock;

    private decimal _todaySalesTotal;
    private int _todaySalesCount;
    private int _lowStockCount;

    public DashboardViewModel(IServiceScopeFactory scopeFactory, PosSession session, IClock clock)
    {
        _scopeFactory = scopeFactory;
        _session = session;
        _clock = clock;
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
    }

    public decimal TodaySalesTotal { get => _todaySalesTotal; set => SetProperty(ref _todaySalesTotal, value); }
    public int TodaySalesCount { get => _todaySalesCount; set => SetProperty(ref _todaySalesCount, value); }
    public int LowStockCount { get => _lowStockCount; set => SetProperty(ref _lowStockCount, value); }

    public ObservableCollection<LowStockRow> LowStockItems { get; } = new();

    public AsyncRelayCommand RefreshCommand { get; }

    public async Task LoadAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
        var inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();

        var since = _clock.UtcNow.Date;
        var todayTotals = await db.Sales.AsNoTracking()
            .Where(s => s.SchoolId == _session.SchoolId && s.CreatedAtUtc >= since)
            .Select(s => s.Total)
            .ToListAsync();

        TodaySalesTotal = todayTotals.Sum();
        TodaySalesCount = todayTotals.Count;

        var low = await inventory.GetLowStockAsync(_session.SchoolId);
        LowStockCount = low.Count;
        LowStockItems.Clear();
        foreach (var p in low)
            LowStockItems.Add(new LowStockRow(p.Name, p.StockOnHand, p.MinStock));
    }
}

/// <summary>Fila de alerta de bajo inventario.</summary>
public sealed record LowStockRow(string Name, decimal StockOnHand, decimal MinStock);
