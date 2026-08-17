using System.Globalization;

namespace SchoolPOS.Domain.Common;

/// <summary>
/// Zona horaria y cultura de México, compartidas por <b>todas</b> las interfaces (portal y POS).
/// Los sellos de tiempo se guardan en UTC, pero el operador piensa en días locales: si un rango
/// que él eligió se manda a la consulta como si ya fuera UTC, el corte del día se desplaza seis
/// horas y las dos consolas reportan cifras distintas sobre los mismos datos.
/// <para>
/// Vive en el dominio, no en un host, justamente para que POS y portal no puedan divergir.
/// </para>
/// </summary>
public static class MxTime
{
    public static readonly TimeZoneInfo Tz = Resolve();
    public static readonly CultureInfo EsMx = CultureInfo.GetCultureInfo("es-MX");

    public static DateTime Local(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Tz);

    /// <summary>Convierte una fecha local (sin zona) al instante UTC correspondiente.</summary>
    public static DateTime ToUtc(DateTime localUnspecified) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localUnspecified, DateTimeKind.Unspecified), Tz);

    /// <summary>El día local en curso, a partir del instante UTC actual.</summary>
    public static DateTime TodayLocal(DateTime utcNow) => Local(utcNow).Date;

    /// <summary>
    /// Instante UTC en que empieza el día local elegido. <c>null</c> se propaga como "sin límite".
    /// </summary>
    public static DateTime? StartOfDayUtc(DateTime? localDate) =>
        localDate is { } d ? ToUtc(d.Date) : null;

    /// <summary>
    /// Último instante UTC del día local elegido (fin inclusivo, como esperan los reportes).
    /// <c>null</c> se propaga como "sin límite".
    /// </summary>
    public static DateTime? EndOfDayUtc(DateTime? localDate) =>
        localDate is { } d ? ToUtc(d.Date.AddDays(1)).AddTicks(-1) : null;

    private static TimeZoneInfo Resolve()
    {
        foreach (var id in new[] { "America/Mexico_City", "Central Standard Time (Mexico)" })
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { }
        return TimeZoneInfo.Utc;
    }
}
