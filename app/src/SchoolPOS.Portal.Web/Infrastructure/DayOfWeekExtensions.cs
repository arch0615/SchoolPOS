using System.Globalization;

namespace SchoolPOS.Portal.Web.Infrastructure;

/// <summary>Nombre del día en español, para el menú semanal (catálogo del portal y su edición).</summary>
public static class DayOfWeekExtensions
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-MX");

    public static string ToSpanish(this DayOfWeek day)
    {
        var name = Es.DateTimeFormat.GetDayName(day);
        return char.ToUpper(name[0], Es) + name[1..];
    }
}
