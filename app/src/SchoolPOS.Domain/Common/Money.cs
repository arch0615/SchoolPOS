using System.Globalization;

namespace SchoolPOS.Domain.Common;

/// <summary>
/// Valor monetario inmutable. Siempre usa <see cref="decimal"/> (nunca float) para
/// cumplir NFR-4 (exactitud financiera, sin deriva por redondeo). El importe se
/// mantiene con la escala de almacenamiento (4 decimales) y se redondea a 2
/// decimales para presentación/liquidación con <see cref="Round"/>.
/// </summary>
public readonly record struct Money : IComparable<Money>
{
    /// <summary>Decimales usados para exhibir/liquidar (centavos).</summary>
    public const int DisplayScale = 2;

    /// <summary>Política de redondeo del sistema: comercial (mitad hacia arriba).</summary>
    public const MidpointRounding Rounding = MidpointRounding.AwayFromZero;

    /// <summary>Código ISO de moneda (p. ej. "MXN", "USD"). Configurable por escuela.</summary>
    public string Currency { get; }

    /// <summary>Importe. Puede ser negativo (p. ej. movimientos de cargo en el libro mayor).</summary>
    public decimal Amount { get; }

    public Money(decimal amount, string currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("La moneda es obligatoria.", nameof(currency));
        Currency = currency.ToUpperInvariant();
        Amount = amount;
    }

    public static Money Zero(string currency) => new(0m, currency);

    /// <summary>Redondea a 2 decimales con la política del sistema.</summary>
    public Money Round() => new(Math.Round(Amount, DisplayScale, Rounding), Currency);

    public bool IsPositive => Amount > 0m;
    public bool IsNegative => Amount < 0m;
    public bool IsZero => Amount == 0m;

    public Money Negate() => new(-Amount, Currency);
    public Money Abs() => new(Math.Abs(Amount), Currency);

    public static Money operator +(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return new Money(a.Amount + b.Amount, a.Currency);
    }

    public static Money operator -(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return new Money(a.Amount - b.Amount, a.Currency);
    }

    public static Money operator *(Money a, decimal factor) => new(a.Amount * factor, a.Currency);

    public int CompareTo(Money other)
    {
        EnsureSameCurrency(this, other);
        return Amount.CompareTo(other.Amount);
    }

    public static bool operator <(Money a, Money b) => a.CompareTo(b) < 0;
    public static bool operator >(Money a, Money b) => a.CompareTo(b) > 0;
    public static bool operator <=(Money a, Money b) => a.CompareTo(b) <= 0;
    public static bool operator >=(Money a, Money b) => a.CompareTo(b) >= 0;

    private static void EnsureSameCurrency(Money a, Money b)
    {
        if (!string.Equals(a.Currency, b.Currency, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"No se pueden operar montos de distinta moneda: {a.Currency} vs {b.Currency}.");
    }

    /// <summary>Formato local (por defecto es-MX).</summary>
    public override string ToString() =>
        Amount.ToString("N2", CultureInfo.GetCultureInfo("es-MX")) + " " + Currency;
}
