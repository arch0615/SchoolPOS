using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Domain.Entities;

/// <summary>
/// Recarga en línea. Nace en el portal (nube) y se aplica al libro mayor local de forma
/// idempotente (dedupe por <see cref="GatewayRef"/>, NFR-7). El estudiante recibe el 100%
/// del monto; la comisión se calcula y persiste por registro (FR-COM-1) y se cobra vía
/// split de Mercado Pago a la cuenta del proveedor (FR-COM-2). Registro inmutable/auditado
/// (FR-COM-7): representa dinero.
/// </summary>
public class TopUp
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SchoolId { get; set; }

    public Guid AccountId { get; set; }
    public Account Account { get; set; } = null!;

    /// <summary>Monto recargado. El estudiante recibe el 100% de este importe.</summary>
    public decimal Amount { get; set; }

    /// <summary>Tasa de comisión vigente al momento de la recarga (instantánea).</summary>
    public decimal CommissionRate { get; set; }

    /// <summary>Comisión calculada = round(Amount * CommissionRate, 2). Se liquida al proveedor.</summary>
    public decimal CommissionAmount { get; set; }

    /// <summary>Referencia única de la pasarela (Mercado Pago). Clave de deduplicación.</summary>
    public string GatewayRef { get; set; } = string.Empty;

    public TopUpStatus Status { get; set; } = TopUpStatus.Pending;

    /// <summary>True cuando ya fue acreditada al libro mayor local (evita doble aplicación).</summary>
    public bool AppliedLocally { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? AppliedAtUtc { get; set; }
}
