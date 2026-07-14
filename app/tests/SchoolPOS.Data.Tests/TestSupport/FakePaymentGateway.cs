using SchoolPOS.Domain.Abstractions;

namespace SchoolPOS.Data.Tests.TestSupport;

/// <summary>
/// Pasarela de pago falsa para pruebas: genera una referencia determinista a partir del Id de la
/// recarga y captura el <see cref="PaymentIntent"/> (para verificar que la comisión se envía como
/// split). No hace llamadas de red.
/// </summary>
public sealed class FakePaymentGateway : IPaymentGateway
{
    public PaymentIntent? LastIntent { get; private set; }

    public Task<PaymentPreference> CreatePreferenceAsync(PaymentIntent intent, CancellationToken ct = default)
    {
        LastIntent = intent;
        var gatewayRef = $"MP-{intent.TopUpId:N}";
        return Task.FromResult(new PaymentPreference(gatewayRef, $"https://mp.test/checkout/{gatewayRef}"));
    }

    public Task<PaymentNotification?> VerifyWebhookAsync(WebhookRequest request, CancellationToken ct = default)
        => Task.FromResult<PaymentNotification?>(new PaymentNotification(request.RawBody, PaymentStatus.Approved));
}
