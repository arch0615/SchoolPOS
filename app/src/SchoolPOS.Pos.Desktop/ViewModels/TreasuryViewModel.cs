using System.Collections.ObjectModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolPOS.Data;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Enums;
using SchoolPOS.Pos.Desktop.Infrastructure;

namespace SchoolPOS.Pos.Desktop.ViewModels;

/// <summary>
/// Tesorería (FR-TRE): apertura/cierre de caja con arqueo, movimientos manuales de efectivo y
/// consulta de sesiones recientes. Una sesión por operador a la vez (<see cref="ITreasuryService"/>
/// lo valida). Solo Administrador.
/// </summary>
public sealed class TreasuryViewModel : ViewModelBase, IAsyncLoadable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PosSession _session;

    private string _statusMessage = string.Empty;
    private string _errorMessage = string.Empty;

    private OpenSessionRow? _openSession;
    private decimal _openingFloat;

    private bool _isIncomeSelected = true;
    private decimal _movementAmount;
    private string _movementReason = string.Empty;

    private decimal _countedAmount;

    public TreasuryViewModel(IServiceScopeFactory scopeFactory, PosSession session)
    {
        _scopeFactory = scopeFactory;
        _session = session;

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        OpenSessionCommand = new AsyncRelayCommand(OpenSessionAsync, () => OpenSession is null);
        RegisterMovementCommand = new AsyncRelayCommand(RegisterMovementAsync,
            () => OpenSession is not null && MovementAmount > 0m && MovementReason.Trim().Length > 0);
        CloseSessionCommand = new AsyncRelayCommand(CloseSessionAsync, () => OpenSession is not null);
    }

    public OpenSessionRow? OpenSession
    {
        get => _openSession;
        private set
        {
            if (SetProperty(ref _openSession, value))
            {
                OnPropertyChanged(nameof(HasOpenSession));
                OnPropertyChanged(nameof(NoOpenSession));
                OpenSessionCommand.RaiseCanExecuteChanged();
                RegisterMovementCommand.RaiseCanExecuteChanged();
                CloseSessionCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasOpenSession => OpenSession is not null;
    public bool NoOpenSession => OpenSession is null;

    public decimal OpeningFloat { get => _openingFloat; set => SetProperty(ref _openingFloat, value); }
    public AsyncRelayCommand OpenSessionCommand { get; }

    public ObservableCollection<CashMovementRow> Movements { get; } = new();

    public bool IsIncomeSelected
    {
        get => _isIncomeSelected;
        set { if (SetProperty(ref _isIncomeSelected, value)) OnPropertyChanged(nameof(IsExpenseSelected)); }
    }

    public bool IsExpenseSelected
    {
        get => !_isIncomeSelected;
        set => IsIncomeSelected = !value;
    }

    private CashMovementType MovementType => IsIncomeSelected ? CashMovementType.Income : CashMovementType.Expense;

    public decimal MovementAmount
    {
        get => _movementAmount;
        set { if (SetProperty(ref _movementAmount, value)) RegisterMovementCommand.RaiseCanExecuteChanged(); }
    }

    public string MovementReason
    {
        get => _movementReason;
        set { if (SetProperty(ref _movementReason, value)) RegisterMovementCommand.RaiseCanExecuteChanged(); }
    }

    public AsyncRelayCommand RegisterMovementCommand { get; }

    public decimal CountedAmount { get => _countedAmount; set => SetProperty(ref _countedAmount, value); }
    public AsyncRelayCommand CloseSessionCommand { get; }

    public ObservableCollection<ClosedSessionRow> RecentSessions { get; } = new();

    public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }
    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }

    public AsyncRelayCommand RefreshCommand { get; }

    public async Task LoadAsync()
    {
        ErrorMessage = string.Empty;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
            var operatorId = _session.Operator!.Id;

            var open = await db.CashSessions.AsNoTracking()
                .Include(s => s.Movements)
                .Where(s => s.SchoolId == _session.SchoolId && s.OperatorId == operatorId && s.Status == CashSessionStatus.Open)
                .OrderByDescending(s => s.OpenedAtUtc)
                .FirstOrDefaultAsync();

            Movements.Clear();
            if (open is null)
            {
                OpenSession = null;
            }
            else
            {
                OpenSession = new OpenSessionRow(open.Id, open.OpeningFloat, open.OpenedAtUtc);
                foreach (var m in open.Movements.OrderByDescending(m => m.CreatedAtUtc))
                    Movements.Add(new CashMovementRow(m.Type, m.Amount, m.Reason, m.CreatedAtUtc));
            }

            var recent = await db.CashSessions.AsNoTracking()
                .Where(s => s.SchoolId == _session.SchoolId && s.Status == CashSessionStatus.Closed)
                .OrderByDescending(s => s.ClosedAtUtc)
                .Take(20)
                .Select(s => new ClosedSessionRow(
                    s.OpenedAtUtc, s.ClosedAtUtc, s.OpeningFloat,
                    s.CountedAmount ?? 0m, s.ExpectedAmount ?? 0m, s.Variance ?? 0m))
                .ToListAsync();
            RecentSessions.Clear();
            foreach (var r in recent)
                RecentSessions.Add(r);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"No se pudo cargar la tesorería: {ex.Message}";
        }
    }

    private async Task OpenSessionAsync()
    {
        if (OpenSession is not null)
            return;

        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var treasury = scope.ServiceProvider.GetRequiredService<ITreasuryService>();
            await treasury.OpenSessionAsync(_session.SchoolId, _session.Operator!.Id, OpeningFloat);
            StatusMessage = $"Caja abierta con fondo {OpeningFloat:C2}.";
            OpeningFloat = 0m;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"No se pudo abrir la caja: {ex.Message}";
        }
    }

    private async Task RegisterMovementAsync()
    {
        if (OpenSession is null || MovementAmount <= 0m || MovementReason.Trim().Length == 0)
            return;

        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var treasury = scope.ServiceProvider.GetRequiredService<ITreasuryService>();
            await treasury.RegisterMovementAsync(
                OpenSession.Id, MovementType, MovementAmount, MovementReason.Trim(), _session.Operator!.Id);

            StatusMessage = $"Movimiento registrado: {(MovementType == CashMovementType.Income ? "+" : "-")}{MovementAmount:C2}.";
            MovementAmount = 0m;
            MovementReason = string.Empty;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"No se pudo registrar el movimiento: {ex.Message}";
        }
    }

    private async Task CloseSessionAsync()
    {
        if (OpenSession is null)
            return;

        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var treasury = scope.ServiceProvider.GetRequiredService<ITreasuryService>();
            var closed = await treasury.CloseSessionAsync(OpenSession.Id, CountedAmount);

            StatusMessage = $"Caja cerrada. Esperado {closed.ExpectedAmount:C2}, contado {closed.CountedAmount:C2}, " +
                             $"variación {closed.Variance:C2}.";
            CountedAmount = 0m;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"No se pudo cerrar la caja: {ex.Message}";
        }
    }
}

public sealed record OpenSessionRow(Guid Id, decimal OpeningFloat, DateTime OpenedAtUtc);
public sealed record CashMovementRow(CashMovementType Type, decimal Amount, string Reason, DateTime CreatedAtUtc);
public sealed record ClosedSessionRow(
    DateTime OpenedAtUtc, DateTime? ClosedAtUtc, decimal OpeningFloat,
    decimal CountedAmount, decimal ExpectedAmount, decimal Variance);
