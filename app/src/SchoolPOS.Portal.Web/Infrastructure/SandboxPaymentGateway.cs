using SchoolPOS.Domain.Abstractions;

namespace SchoolPOS.Portal.Web.Infrastructure;

/// <summary>
/// Pasarela de pago de <b>caja de arena</b> para desarrollo y demostración: no llama a Mercado
/// Pago. Devuelve una URL de checkout interna (<c>/Payments/Sandbox</c>) donde se simula aprobar o
/// rechazar el pago. Sustituir por la implementación real de Mercado Pago (split + verificación de
/// firma del webhook) en producción, con las credenciales de la escuela.
/// </summary>
public sealed class SandboxPaymentGateway : IPaymentGateway
{
    public Task<PaymentPreference> CreatePreferenceAsync(PaymentIntent intent, CancellationToken ct = default)
    {
        // La comisión (intent.CommissionAmount) viajaría como application_fee del marketplace.
        var gatewayRef = $"SBX-{intent.TopUpId:N}";
        var checkoutUrl = $"/Payments/Sandbox?ref={gatewayRef}&amount={intent.Amount}";
        return Task.FromResult(new PaymentPreference(gatewayRef, checkoutUrl));
    }

    public Task<PaymentNotification?> VerifyWebhookAsync(WebhookRequest request, CancellationToken ct = default)
    {
        // En sandbox el cuerpo es "<gatewayRef>|<approved|rejected>".
        var parts = request.RawBody.Split('|');
        if (parts.Length != 2)
            return Task.FromResult<PaymentNotification?>(null);

        var status = parts[1] == "approved" ? PaymentStatus.Approved : PaymentStatus.Rejected;
        return Task.FromResult<PaymentNotification?>(new PaymentNotification(parts[0], status));
    }
}
