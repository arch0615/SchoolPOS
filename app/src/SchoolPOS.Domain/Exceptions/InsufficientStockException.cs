namespace SchoolPOS.Domain.Exceptions;

/// <summary>
/// Se lanza cuando una salida/venta excede las existencias disponibles del producto (evita
/// stock negativo salvo que se permita explícitamente en un ajuste). Revierte la operación.
/// </summary>
public class InsufficientStockException : Exception
{
    public Guid ProductId { get; }
    public string ProductName { get; }
    public decimal Requested { get; }
    public decimal Available { get; }

    public InsufficientStockException(Guid productId, string productName, decimal requested, decimal available)
        : base($"Existencias insuficientes de \"{productName}\" ({productId}): se requiere {requested}, disponible {available}.")
    {
        ProductId = productId;
        ProductName = productName;
        Requested = requested;
        Available = available;
    }
}
