using System.Globalization;
using System.Windows.Data;
using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Pos.Desktop.Infrastructure;

/// <summary>
/// Muestra el rol en español. Enlazar el enum directamente pinta su nombre en inglés
/// ("Warehouse", "Cashier") en una interfaz que por lo demás está en español.
/// </summary>
public sealed class RoleNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is UserRole role
            ? role switch
            {
                UserRole.Admin => "Administrador",
                UserRole.Warehouse => "Almacén",
                _ => "Cajero",
            }
            : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
