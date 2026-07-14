namespace SchoolPOS.Domain.Common;

/// <summary>
/// Cálculo de la comisión del proveedor sobre recargas en línea (FR-COM-1). La escuela absorbe
/// la comisión: el estudiante recibe el 100% del monto y la comisión (configurable por escuela,
/// default 5%, puede ser 0) se separa a la cuenta del proveedor vía split de Mercado Pago.
/// Es una función pura para poder probarse sin dependencias.
/// </summary>
public static class CommissionCalculator
{
    private const int Scale = 2;
    private const MidpointRounding Rounding = MidpointRounding.AwayFromZero;

    /// <summary>Comisión = redondeo(monto * tasa, 2). El estudiante siempre recibe el monto completo.</summary>
    public static decimal Compute(decimal amount, decimal rate)
    {
        if (amount < 0m)
            throw new ArgumentOutOfRangeException(nameof(amount), "El monto no puede ser negativo.");
        if (rate < 0m || rate > 1m)
            throw new ArgumentOutOfRangeException(nameof(rate), "La tasa de comisión debe estar entre 0 y 1.");

        return Math.Round(amount * rate, Scale, Rounding);
    }
}
