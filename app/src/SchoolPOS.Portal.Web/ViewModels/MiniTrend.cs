namespace SchoolPOS.Portal.Web.ViewModels;

/// <summary>Un punto (fecha, valor) para una mini-gráfica de tendencia diaria.</summary>
public sealed record MiniTrendPoint(DateOnly Date, decimal Value);

/// <summary>
/// Modelo de una mini-gráfica de área/línea de una sola serie (small multiple). Cada gráfica tiene
/// su propio eje — así se comparan dos medidas de escala distinta sin caer en el doble eje.
/// </summary>
public sealed class MiniTrend
{
    public string Title { get; init; } = string.Empty;
    public string ColorHex { get; init; } = "#2563eb";
    public IReadOnlyList<MiniTrendPoint> Points { get; init; } = System.Array.Empty<MiniTrendPoint>();
}
