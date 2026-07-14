using SchoolPOS.Pos.Desktop.Infrastructure;

namespace SchoolPOS.Pos.Desktop.ViewModels;

/// <summary>Renglón del carrito de venta (editable en la cuadrícula).</summary>
public sealed class CartLine : ViewModelBase
{
    private decimal _quantity = 1m;
    private decimal _discount;

    public required Guid ProductId { get; init; }
    public required string Description { get; init; }
    public required decimal UnitPrice { get; init; }

    public decimal Quantity
    {
        get => _quantity;
        set { if (SetProperty(ref _quantity, value)) NotifyTotals(); }
    }

    public decimal Discount
    {
        get => _discount;
        set { if (SetProperty(ref _discount, value)) NotifyTotals(); }
    }

    public decimal LineTotal => Math.Round(Quantity * UnitPrice - Discount, 2, MidpointRounding.AwayFromZero);

    /// <summary>Notifica al view-model contenedor para recalcular los totales.</summary>
    public event Action? Changed;

    private void NotifyTotals()
    {
        OnPropertyChanged(nameof(LineTotal));
        Changed?.Invoke();
    }
}
