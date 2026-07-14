using System.Text;

namespace SchoolPOS.Data.Reporting;

/// <summary>Generación de CSV con escape correcto (comillas, comas, saltos de línea) para exportar reportes.</summary>
public static class Csv
{
    public static string Build(IEnumerable<string> headers, IEnumerable<IEnumerable<string>> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", headers.Select(Escape)));
        foreach (var row in rows)
            sb.AppendLine(string.Join(",", row.Select(Escape)));
        return sb.ToString();
    }

    private static string Escape(string? field)
    {
        field ??= string.Empty;
        var needsQuoting = field.Contains(',') || field.Contains('"')
            || field.Contains('\n') || field.Contains('\r');
        if (!needsQuoting)
            return field;
        return "\"" + field.Replace("\"", "\"\"") + "\"";
    }
}
