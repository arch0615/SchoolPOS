using System.Collections.ObjectModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolPOS.Data;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Enums;
using SchoolPOS.Pos.Desktop.Infrastructure;

namespace SchoolPOS.Pos.Desktop.ViewModels;

/// <summary>
/// Padrón de alumnos (FR-ADM-2): alta, edición y baja. Es la pantalla que faltaba para que una
/// escuela recién instalada pudiera inscribir a alguien; sin ella el saldo, las recargas y el
/// cobro contra saldo eran inalcanzables.
/// </summary>
public sealed class StudentsViewModel : ViewModelBase, IAsyncLoadable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PosSession _session;

    private string _search = string.Empty;
    private bool _includeInactive;
    private StudentRow? _selected;
    private string _enrollmentNo = string.Empty;
    private string _fullName = string.Empty;
    private string _cardCode = string.Empty;
    private string _statusMessage = string.Empty;
    private string _errorMessage = string.Empty;

    public StudentsViewModel(IServiceScopeFactory scopeFactory, PosSession session)
    {
        _scopeFactory = scopeFactory;
        _session = session;

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        NewCommand = new RelayCommand(ClearForm);
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        ToggleActiveCommand = new AsyncRelayCommand(ToggleActiveAsync, () => Selected is not null);
        TopUpCommand = new AsyncRelayCommand(TopUpAsync, () => Selected is not null);
        AdjustCommand = new AsyncRelayCommand(AdjustAsync, () => Selected is not null);
    }

    public ObservableCollection<StudentRow> Students { get; } = new();

    public string Search { get => _search; set => SetProperty(ref _search, value); }

    public bool IncludeInactive
    {
        get => _includeInactive;
        set { if (SetProperty(ref _includeInactive, value)) _ = LoadAsync(); }
    }

    public StudentRow? Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value))
                return;

            // Al elegir un alumno el formulario pasa a modo edición.
            if (value is not null)
            {
                EnrollmentNo = value.EnrollmentNo;
                FullName = value.FullName;
                CardCode = value.CardCode ?? string.Empty;
            }
            OnPropertyChanged(nameof(IsEditing));
            OnPropertyChanged(nameof(SaveButtonText));
            OnPropertyChanged(nameof(ToggleActiveText));
            ToggleActiveCommand.RaiseCanExecuteChanged();
            TopUpCommand.RaiseCanExecuteChanged();
            AdjustCommand.RaiseCanExecuteChanged();
        }
    }

    public bool CanAdjustBalance => _session.CanAdjustBalance;

    public bool IsEditing => Selected is not null;
    public string SaveButtonText => IsEditing ? "Guardar cambios" : "Dar de alta";
    public string ToggleActiveText => Selected is { IsActive: false } ? "Reactivar" : "Dar de baja";

    public string EnrollmentNo { get => _enrollmentNo; set => SetProperty(ref _enrollmentNo, value); }
    public string FullName { get => _fullName; set => SetProperty(ref _fullName, value); }
    public string CardCode { get => _cardCode; set => SetProperty(ref _cardCode, value); }

    public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }
    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }

    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand NewCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }
    public AsyncRelayCommand ToggleActiveCommand { get; }

    public async Task LoadAsync()
    {
        ErrorMessage = string.Empty;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var registry = scope.ServiceProvider.GetRequiredService<IStudentRegistry>();
            var rows = await registry.ListAsync(_session.SchoolId, Search, IncludeInactive);

            Students.Clear();
            foreach (var r in rows)
                Students.Add(r);

            await RefreshOpenTillAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"No se pudo cargar el padrón: {ex.Describe()}";
        }
    }

    private void ClearForm()
    {
        Selected = null;
        EnrollmentNo = string.Empty;
        FullName = string.Empty;
        CardCode = string.Empty;
        StatusMessage = string.Empty;
        ErrorMessage = string.Empty;
    }

    private async Task SaveAsync()
    {
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var registry = scope.ServiceProvider.GetRequiredService<IStudentRegistry>();

            if (Selected is { } current)
            {
                await registry.UpdateAsync(current.StudentId, EnrollmentNo, FullName, CardCode);
                StatusMessage = $"Alumno '{FullName}' actualizado.";
            }
            else
            {
                await registry.CreateAsync(_session.SchoolId, EnrollmentNo, FullName, CardCode);
                StatusMessage = $"Alumno '{FullName}' dado de alta con saldo $0.00.";
            }

            ClearForm();
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Describe();
        }
    }

    private async Task ToggleActiveAsync()
    {
        if (Selected is not { } current)
            return;

        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var registry = scope.ServiceProvider.GetRequiredService<IStudentRegistry>();
            await registry.SetActiveAsync(current.StudentId, !current.IsActive);
            StatusMessage = current.IsActive
                ? $"'{current.FullName}' dado de baja. Su historial se conserva."
                : $"'{current.FullName}' reactivado.";
            ClearForm();
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Describe();
        }
    }

    // ---- Recarga en efectivo ----

    private decimal _topUpAmount;
    private Guid? _cashSessionId;

    /// <summary>Monto a recargar en efectivo al alumno seleccionado.</summary>
    public decimal TopUpAmount { get => _topUpAmount; set => SetProperty(ref _topUpAmount, value); }

    /// <summary>Aviso cuando no hay caja abierta: el efectivo no podría quedar en el arqueo.</summary>
    public bool NeedsOpenTill => _cashSessionId is null;

    public AsyncRelayCommand TopUpCommand { get; private set; } = null!;

    /// <summary>Caja abierta del operador; la recarga en efectivo la exige.</summary>
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

    private async Task TopUpAsync()
    {
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;

        if (Selected is not { } student)
        {
            ErrorMessage = "Elija un alumno de la lista.";
            return;
        }
        if (TopUpAmount <= 0m)
        {
            ErrorMessage = "Escriba el monto que está recibiendo.";
            return;
        }

        // Relectura por si acaba de abrir la caja sin volver a esta pantalla.
        if (_cashSessionId is null)
            await RefreshOpenTillAsync();
        if (_cashSessionId is null)
        {
            ErrorMessage = "Abra su caja en Tesorería: el efectivo debe quedar registrado en el arqueo.";
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var cash = scope.ServiceProvider.GetRequiredService<ICashTopUpService>();
            await cash.CreateAsync(
                _session.SchoolId, student.AccountId, TopUpAmount, _session.Operator!.Id, _cashSessionId.Value);

            StatusMessage = $"Recarga de {TopUpAmount:C2} aplicada a {student.FullName}.";
            TopUpAmount = 0m;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Describe();
        }
    }

    // ---- Ajuste manual de saldo (FR-ADM-2) ----
    // Para corregir un saldo mal aplicado (recarga con el monto equivocado, cargo duplicado, etc.)
    // sin forzar una venta/devolución que no corresponde al error real. No toca caja/arqueo: es un
    // asiento directo al libro mayor, con motivo obligatorio para la bitácora (acción sensible,
    // FR-ADM-4) — por eso solo el administrador la ve (PosSession.CanAdjustBalance).

    private decimal _adjustAmount;
    private string _adjustReason = string.Empty;

    /// <summary>Importe con signo: positivo abona, negativo carga. Nunca puede ser cero.</summary>
    public decimal AdjustAmount { get => _adjustAmount; set => SetProperty(ref _adjustAmount, value); }

    /// <summary>Motivo del ajuste, obligatorio: queda en la bitácora junto al monto y quién lo aplicó.</summary>
    public string AdjustReason { get => _adjustReason; set => SetProperty(ref _adjustReason, value); }

    public AsyncRelayCommand AdjustCommand { get; private set; } = null!;

    private async Task AdjustAsync()
    {
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;

        if (Selected is not { } student)
        {
            ErrorMessage = "Elija un alumno de la lista.";
            return;
        }
        if (AdjustAmount == 0m)
        {
            ErrorMessage = "Escriba el importe del ajuste (positivo abona, negativo carga).";
            return;
        }
        if (string.IsNullOrWhiteSpace(AdjustReason))
        {
            ErrorMessage = "Escriba el motivo del ajuste: queda en la bitácora.";
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var balance = scope.ServiceProvider.GetRequiredService<IBalanceService>();
            await balance.AdjustAsync(
                student.AccountId, AdjustAmount, AdjustReason.Trim(), _session.Operator!.Id);

            StatusMessage = $"Ajuste de {AdjustAmount:C2} aplicado a {student.FullName}.";
            AdjustAmount = 0m;
            AdjustReason = string.Empty;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Describe();
        }
    }
}
