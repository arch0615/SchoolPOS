using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Pos.Desktop.Infrastructure;

namespace SchoolPOS.Pos.Desktop.ViewModels;

/// <summary>Visor de bitácora (FR-ADM-4): acciones sensibles filtrables por fecha y acción.</summary>
public sealed class AuditViewModel : ViewModelBase, IAsyncLoadable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PosSession _session;

    private DateTime? _from;
    private DateTime? _to;
    private string _actionFilter = string.Empty;

    public AuditViewModel(IServiceScopeFactory scopeFactory, PosSession session)
    {
        _scopeFactory = scopeFactory;
        _session = session;
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
    }

    public DateTime? From { get => _from; set => SetProperty(ref _from, value); }
    public DateTime? To { get => _to; set => SetProperty(ref _to, value); }
    public string ActionFilter { get => _actionFilter; set => SetProperty(ref _actionFilter, value); }

    public ObservableCollection<AuditEntryRow> Entries { get; } = new();

    public AsyncRelayCommand RefreshCommand { get; }

    public async Task LoadAsync()
    {
        var fromUtc = From?.Date;
        var toUtc = To?.Date.AddDays(1).AddTicks(-1);
        var action = string.IsNullOrWhiteSpace(ActionFilter) ? null : ActionFilter.Trim();

        using var scope = _scopeFactory.CreateScope();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLogQueryService>();
        var rows = await audit.QueryAsync(_session.SchoolId, fromUtc, toUtc, action);

        Entries.Clear();
        foreach (var r in rows)
            Entries.Add(r);
    }
}
