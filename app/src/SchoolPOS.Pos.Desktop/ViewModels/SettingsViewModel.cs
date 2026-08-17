using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolPOS.Data;
using SchoolPOS.Pos.Desktop.Infrastructure;

namespace SchoolPOS.Pos.Desktop.ViewModels;

/// <summary>
/// Configuración de la escuela (FR-ADM-3): comisión, IVA y datos fiscales para el CFDI de
/// comisión (Escuela.Rfc/LegalName/TaxRegime/PostalCode/CfdiUse) — los únicos parámetros de
/// negocio que viven en la base de datos, no en appsettings.json (claves de Mercado Pago/SW
/// Sapien/SMTP siguen siendo de despliegue, no editables aquí). Solo Administrador.
/// </summary>
public sealed class SettingsViewModel : ViewModelBase, IAsyncLoadable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PosSession _session;

    private string _schoolName = string.Empty;
    private string _currency = "MXN";
    private decimal _commissionRate;
    private decimal _taxRate;
    private bool _taxInclusive = true;
    private string _rfc = string.Empty;
    private string _legalName = string.Empty;
    private string _taxRegime = string.Empty;
    private string _postalCode = string.Empty;
    private string _cfdiUse = string.Empty;

    private string _statusMessage = string.Empty;
    private string _errorMessage = string.Empty;

    public SettingsViewModel(IServiceScopeFactory scopeFactory, PosSession session)
    {
        _scopeFactory = scopeFactory;
        _session = session;
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        SaveCommand = new AsyncRelayCommand(SaveAsync);
    }

    public string SchoolName { get => _schoolName; private set => SetProperty(ref _schoolName, value); }
    public string Currency { get => _currency; set => SetProperty(ref _currency, value); }

    /// <summary>
    /// Comisión pactada con el proveedor. Se muestra pero <b>no se edita aquí</b>: la tarifa es del
    /// contrato, la fija el proveedor desde su panel, y la recarga la cobra el portal contra la DB
    /// de la nube — este valor local ni siquiera intervendría en el cálculo, así que un campo
    /// editable solo aparentaría hacer algo.
    /// </summary>
    public decimal CommissionRate { get => _commissionRate; private set => SetProperty(ref _commissionRate, value); }

    /// <summary>La comisión en porcentaje, para mostrarla legible (5% en vez de 0.05).</summary>
    public string CommissionRateText => (CommissionRate * 100m).ToString("0.##") + " %";

    public decimal TaxRate { get => _taxRate; set => SetProperty(ref _taxRate, value); }
    public bool TaxInclusive { get => _taxInclusive; set => SetProperty(ref _taxInclusive, value); }

    public string Rfc { get => _rfc; set => SetProperty(ref _rfc, value); }
    public string LegalName { get => _legalName; set => SetProperty(ref _legalName, value); }
    public string TaxRegime { get => _taxRegime; set => SetProperty(ref _taxRegime, value); }
    public string PostalCode { get => _postalCode; set => SetProperty(ref _postalCode, value); }
    public string CfdiUse { get => _cfdiUse; set => SetProperty(ref _cfdiUse, value); }

    public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }
    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }

    public async Task LoadAsync()
    {
        ErrorMessage = string.Empty;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();

            var school = await db.Schools.AsNoTracking().FirstOrDefaultAsync(s => s.Id == _session.SchoolId);
            if (school is null)
            {
                ErrorMessage = "No se encontró la configuración de la escuela.";
                return;
            }

            SchoolName = school.Name;
            Currency = school.Currency;
            CommissionRate = school.CommissionRate;
            OnPropertyChanged(nameof(CommissionRateText));
            TaxRate = school.TaxRate;
            TaxInclusive = school.TaxInclusive;
            Rfc = school.Rfc ?? string.Empty;
            LegalName = school.LegalName ?? string.Empty;
            TaxRegime = school.TaxRegime ?? string.Empty;
            PostalCode = school.PostalCode ?? string.Empty;
            CfdiUse = school.CfdiUse ?? string.Empty;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"No se pudo cargar la configuración: {ex.Message}";
        }
    }

    private async Task SaveAsync()
    {
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
            var school = await db.Schools.FirstOrDefaultAsync(s => s.Id == _session.SchoolId)
                ?? throw new InvalidOperationException("No se encontró la configuración de la escuela.");

            // CommissionRate no se toca: la fija el proveedor desde su panel.
            school.Currency = Currency.Trim().ToUpperInvariant();
            school.TaxRate = TaxRate;
            school.TaxInclusive = TaxInclusive;
            school.Rfc = string.IsNullOrWhiteSpace(Rfc) ? null : Rfc.Trim();
            school.LegalName = string.IsNullOrWhiteSpace(LegalName) ? null : LegalName.Trim();
            school.TaxRegime = string.IsNullOrWhiteSpace(TaxRegime) ? null : TaxRegime.Trim();
            school.PostalCode = string.IsNullOrWhiteSpace(PostalCode) ? null : PostalCode.Trim();
            school.CfdiUse = string.IsNullOrWhiteSpace(CfdiUse) ? null : CfdiUse.Trim();

            await db.SaveChangesAsync();
            StatusMessage = "Configuración guardada.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"No se pudo guardar la configuración: {ex.Message}";
        }
    }
}
