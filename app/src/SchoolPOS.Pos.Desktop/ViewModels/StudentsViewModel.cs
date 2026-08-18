using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using SchoolPOS.Domain.Abstractions;
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
        }
    }

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
        }
        catch (Exception ex)
        {
            ErrorMessage = $"No se pudo cargar el padrón: {ex.Message}";
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
            ErrorMessage = ex.Message;
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
            ErrorMessage = ex.Message;
        }
    }
}
