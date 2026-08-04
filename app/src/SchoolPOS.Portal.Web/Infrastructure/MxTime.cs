using System.Globalization;

namespace SchoolPOS.Portal.Web.Infrastructure;

/// <summary>Zona horaria y cultura de México, compartidas por las vistas y modelos del portal.</summary>
public static class MxTime
{
    public static readonly TimeZoneInfo Tz = Resolve();
    public static readonly CultureInfo EsMx = CultureInfo.GetCultureInfo("es-MX");

    public static DateTime Local(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Tz);

    /// <summary>Convierte una fecha local (sin zona) al instante UTC correspondiente.</summary>
    public static DateTime ToUtc(DateTime localUnspecified) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localUnspecified, DateTimeKind.Unspecified), Tz);

    private static TimeZoneInfo Resolve()
    {
        foreach (var id in new[] { "America/Mexico_City", "Central Standard Time (Mexico)" })
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { }
        return TimeZoneInfo.Utc;
    }
}
