using SchoolPOS.Domain.Entities;
using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Domain.Abstractions;

/// <summary>Renglón solicitado en una venta.</summary>
public sealed record SaleLineRequest(
    Guid ProductId,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal Discount = 0m);

/// <summary>Solicitud de venta que arma el POS antes de cobrar.</summary>
public sealed record SaleRequest(
    Guid SchoolId,
    Guid CashierId,
    TenderType Tender,
    IReadOnlyList<SaleLineRequest> Lines,
    Guid? StudentId = null,
    Guid? AccountId = null,
    Guid? CashSessionId = null);

/// <summary>
/// Servicio de ventas. Registra la venta, descuenta inventario y, si el cobro es por saldo,
/// aplica el cargo al libro mayor — todo en una <b>sola transacción atómica</b>. Si algo falla
/// (stock o saldo insuficiente), no se registra nada.
/// </summary>
public interface ISalesService
{
    /// <summary>
    /// Cobra una venta. Calcula totales con el impuesto configurado por escuela, descuenta stock
    /// de cada renglón y, para cobro por saldo, debita la cuenta del estudiante. Devuelve la venta
    /// persistida con sus renglones.
    /// </summary>
    Task<Sale> RegisterSaleAsync(SaleRequest request, CancellationToken ct = default);

    /// <summary>
    /// Devolución total o parcial de una venta (FR-SAL-5): reintegra saldo (o registra la
    /// devolución en efectivo) y reingresa stock, dejando traza. <paramref name="lines"/> indica
    /// qué renglones y cantidades devolver.
    /// </summary>
    Task<Sale> RefundSaleAsync(
        Guid saleId, IReadOnlyList<(Guid SaleLineId, decimal Quantity)> lines, Guid operatorId,
        CancellationToken ct = default);
}
