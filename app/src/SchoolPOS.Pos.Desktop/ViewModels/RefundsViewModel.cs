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
/// Devoluciones (FR-SAL-5). Busca una venta reciente, permite elegir qué renglones y cuántas
/// piezas devolver, y ejecuta la devolución: reingresa stock y reintegra el importe — al saldo del
/// alumno si se cobró por saldo, o como egreso de la caja abierta si se cobró en efectivo.
/// Solo administrador (<see cref="PosSession.CanRefund"/>).
/// </summary>
public sealed class RefundsViewModel : ViewModelBase, IAsyncLoadable
{
    /// <summary>Ventana de búsqueda por defecto: lo que un mostrador devuelve en la práctica.</summary>
    private const int LookbackDays = 30;
    private const int MaxSales = 100;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PosSession _session;
    private readonly IClock _clock;

    private SaleRow? _selectedSale;
    private string _search = string.Empty;
    private string _statusMessage = string.Empty;
    private string _errorMessage = string.Empty;
    private Guid? _cashSessionId;

    public RefundsViewModel(IServiceScopeFactory scopeFactory, PosSession session, IClock clock)
    {
        _scopeFactory = scopeFactory;
        _session = session;
        _clock = clock;

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        RefundCommand = new AsyncRelayCommand(RefundAsync, () => SelectedSale is not null);
    }

    public ObservableCollection<SaleRow> Sales { get; } = new();
    public ObservableCollection<RefundLine> Lines { get; } = new();

    /// <summary>Filtro por alumno o folio; vacío muestra todas las ventas recientes.</summary>
    public string Search
    {
        get => _search;
        set => SetProperty(ref _search, value);
    }

    public SaleRow? SelectedSale
    {
        get => _selectedSale;
        set
        {
            if (SetProperty(ref _selectedSale, value))
            {
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(SelectedTenderText));
                RefundCommand.RaiseCanExecuteChanged();
                _ = LoadLinesAsync();
            }
        }
    }

    public bool HasSelection => SelectedSale is not null;

    public string SelectedTenderText => SelectedSale is null
        ? "—"
        : SelectedSale.Tender == TenderType.Cash ? "Efectivo (sale de la caja)" : "Saldo del alumno";

    /// <summary>Aviso cuando la venta fue en efectivo y el operador no tiene caja abierta.</summary>
    public bool NeedsOpenTill =>
        SelectedSale is { Tender: TenderType.Cash } && _cashSessionId is null;

    public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }
    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand RefundCommand { get; }

    public async Task LoadAsync()
    {
        ErrorMessage = string.Empty;
        SelectedSale = null;
        Lines.Clear();

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();

            var since = MxTime.ToUtc(MxTime.TodayLocal(_clock.UtcNow).AddDays(-LookbackDays));
            var term = Search.Trim();

            // Ya devueltas por completo no se listan: no queda nada que devolver en ellas.
            // LEFT JOIN al alumno porque una venta en efectivo puede no tener alumno asociado.
            var query =
                from s in db.Sales.AsNoTracking()
                where s.SchoolId == _session.SchoolId
                      && s.CreatedAtUtc >= since
                      && s.Status != SaleStatus.Refunded
                join st in db.Students.AsNoTracking() on s.StudentId equals st.Id into students
                from st in students.DefaultIfEmpty()
                where term == "" || (st != null && (st.FullName.Contains(term) || st.EnrollmentNo.Contains(term)))
                orderby s.CreatedAtUtc descending
                select new
                {
                    s.Id,
                    s.CreatedAtUtc,
                    s.Total,
                    s.Tender,
                    s.Status,
                    StudentName = st != null ? st.FullName : null,
                };

            var rows = await query.Take(MaxSales).ToListAsync();

            Sales.Clear();
            foreach (var r in rows)
                Sales.Add(new SaleRow(
                    r.Id, MxTime.Local(r.CreatedAtUtc), r.Total, r.Tender, r.Status,
                    r.StudentName ?? "Mostrador"));

            await RefreshOpenTillAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"No se pudieron cargar las ventas: {ex.Message}";
        }
    }

    private async Task LoadLinesAsync()
    {
        Lines.Clear();
        StatusMessage = string.Empty;
        if (SelectedSale is null)
        {
            OnPropertyChanged(nameof(NeedsOpenTill));
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();

            var saleId = SelectedSale.Id;
            var lines = await db.SaleLines.AsNoTracking()
                .Where(l => l.SaleId == saleId)
                .OrderBy(l => l.Description)
                .ToListAsync();

            foreach (var l in lines)
            {
                // Solo tiene sentido mostrar lo que aún se puede devolver.
                var refundable = l.Quantity - l.QuantityRefunded;
                if (refundable > 0m)
                    Lines.Add(new RefundLine(l.Id, l.Description, l.Quantity, refundable, l.LineTotal));
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"No se pudieron cargar los renglones: {ex.Message}";
        }

        OnPropertyChanged(nameof(NeedsOpenTill));
    }

    /// <summary>Caja abierta del operador: obligatoria para devolver una venta en efectivo.</summary>
    private async Task RefreshOpenTillAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
            var operatorId = _session.Operator!.Id;

            _cashSessionId = await db.CashSessions.AsNoTracking()
                .Where(s => s.SchoolId == _session.SchoolId
                            && s.OperatorId == operatorId
                            && s.Status == CashSessionStatus.Open)
                .OrderByDescending(s => s.OpenedAtUtc)
                .Select(s => (Guid?)s.Id)
                .FirstOrDefaultAsync();
        }
        catch (Exception)
        {
            _cashSessionId = null;
        }

        OnPropertyChanged(nameof(NeedsOpenTill));
    }

    private async Task RefundAsync()
    {
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;

        if (SelectedSale is null)
            return;

        var toRefund = Lines
            .Where(l => l.QuantityToRefund > 0m)
            .Select(l => (l.SaleLineId, l.QuantityToRefund))
            .ToList();

        if (toRefund.Count == 0)
        {
            ErrorMessage = "Indique cuántas piezas devolver en al menos un renglón.";
            return;
        }

        var overflow = Lines.FirstOrDefault(l => l.QuantityToRefund > l.Refundable);
        if (overflow is not null)
        {
            ErrorMessage = $"'{overflow.Description}': no se pueden devolver más de {overflow.Refundable:0.##}.";
            return;
        }

        if (SelectedSale.Tender == TenderType.Cash)
        {
            await RefreshOpenTillAsync();
            if (_cashSessionId is null)
            {
                ErrorMessage = "Abra su caja en Tesorería: la devolución en efectivo sale del cajón.";
                return;
            }
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var sales = scope.ServiceProvider.GetRequiredService<ISalesService>();

            var sale = await sales.RefundSaleAsync(
                SelectedSale.Id, toRefund, _session.Operator!.Id,
                SelectedSale.Tender == TenderType.Cash ? _cashSessionId : null);

            StatusMessage = sale.Status == SaleStatus.Refunded
                ? "Devolución total registrada."
                : "Devolución parcial registrada.";

            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"No se pudo registrar la devolución: {ex.Message}";
        }
    }
}

/// <summary>Venta candidata a devolución.</summary>
public sealed record SaleRow(
    Guid Id, DateTime CreatedAtLocal, decimal Total, TenderType Tender, SaleStatus Status, string StudentName)
{
    public string TenderText => Tender == TenderType.Cash ? "Efectivo" : "Saldo";
    public string StatusText => Status == SaleStatus.PartiallyRefunded ? "Devuelta en parte" : "Completada";
}

/// <summary>Renglón devolvible, con la cantidad que el operador captura.</summary>
public sealed class RefundLine : ViewModelBase
{
    private decimal _quantityToRefund;

    public RefundLine(Guid saleLineId, string description, decimal quantity, decimal refundable, decimal lineTotal)
    {
        SaleLineId = saleLineId;
        Description = description;
        Quantity = quantity;
        Refundable = refundable;
        LineTotal = lineTotal;
    }

    public Guid SaleLineId { get; }
    public string Description { get; }
    public decimal Quantity { get; }
    public decimal Refundable { get; }
    public decimal LineTotal { get; }

    public decimal QuantityToRefund
    {
        get => _quantityToRefund;
        set => SetProperty(ref _quantityToRefund, value);
    }
}
