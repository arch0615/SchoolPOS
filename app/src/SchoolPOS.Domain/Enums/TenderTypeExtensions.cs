namespace SchoolPOS.Domain.Enums;

/// <summary>Etiqueta en español de cada forma de cobro, para pantallas y reportes.</summary>
public static class TenderTypeExtensions
{
    public static string ToSpanish(this TenderType tender) => tender switch
    {
        TenderType.Balance => "Saldo",
        TenderType.Cash => "Efectivo",
        TenderType.CreditCard => "Tarjeta de crédito",
        TenderType.Other => "Otro",
        _ => tender.ToString(),
    };
}
