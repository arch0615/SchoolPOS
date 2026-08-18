using System.Windows;

namespace SchoolPOS.Pos.Desktop.Infrastructure;

/// <summary>
/// Puente para enlazar propiedades del view-model desde una <c>DataGridColumn</c>. Las columnas de
/// un DataGrid no forman parte del árbol visual, así que no heredan el DataContext y un
/// <c>RelativeSource AncestorType=UserControl</c> nunca resuelve: el enlace falla en silencio y la
/// propiedad se queda en su valor por omisión. Ahí se perdía el bloqueo de descuentos por rol
/// (IsReadOnly quedaba en false y cualquier cajero podía capturar un descuento).
/// Al ser <see cref="Freezable"/>, sí hereda el DataContext y sirve como fuente estable.
/// </summary>
public sealed class BindingProxy : Freezable
{
    protected override Freezable CreateInstanceCore() => new BindingProxy();

    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(nameof(Data), typeof(object), typeof(BindingProxy));

    public object? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }
}
