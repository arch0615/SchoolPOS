using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using SchoolPOS.Domain.Abstractions;

namespace SchoolPOS.Payments.MercadoPago;

/// <summary>
/// Implementación real de <see cref="IPaymentGateway"/> sobre Mercado Pago (Checkout Pro +
/// marketplace). Crea una preferencia de pago con <c>marketplace_fee</c> = comisión (split
/// automático a la cuenta del proveedor, FR-COM-2) y verifica el webhook validando la firma y
/// consultando el pago server-side (NFR-3). Requiere credenciales reales (token del vendedor por
/// OAuth marketplace y clave secreta de webhook).
/// </summary>
public sealed class MercadoPagoGateway : IPaymentGateway
{
    private readonly HttpClient _http;
    private readonly MercadoPagoOptions _options;

    public MercadoPagoGateway(HttpClient http, MercadoPagoOptions options)
    {
        _http = http;
        _options = options;
        if (_http.BaseAddress is null && !string.IsNullOrEmpty(_options.BaseUrl))
            _http.BaseAddress = new Uri(_options.BaseUrl);
    }

    public async Task<PaymentPreference> CreatePreferenceAsync(PaymentIntent intent, CancellationToken ct = default)
    {
        var body = new
        {
            items = new[]
            {
                new
                {
                    title = intent.Description,
                    quantity = 1,
                    unit_price = intent.Amount,
                    currency_id = intent.Currency,
                },
            },
            external_reference = intent.TopUpId.ToString(),
            marketplace_fee = intent.CommissionAmount, // split: comisión → cuenta del proveedor
            notification_url = _options.NotificationUrl,
            back_urls = new
            {
                success = _options.SuccessUrl,
                failure = _options.FailureUrl,
                pending = _options.PendingUrl,
            },
            auto_return = "approved",
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/checkout/preferences")
        {
            Content = JsonContent.Create(body),
        };
        Authorize(request);

        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var preference = await response.Content.ReadFromJsonAsync<PreferenceResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Respuesta vacía al crear la preferencia de Mercado Pago.");

        // La referencia con la que conciliamos es nuestra external_reference (el Id de la recarga).
        return new PaymentPreference(intent.TopUpId.ToString(), preference.InitPoint);
    }

    public async Task<PaymentNotification?> VerifyWebhookAsync(WebhookRequest request, CancellationToken ct = default)
    {
        // 1) Validar la firma del webhook (rechaza notificaciones no auténticas).
        if (!MercadoPagoSignatureVerifier.Verify(
                request.Signature, request.RequestId, request.ResourceId, _options.WebhookSecret))
            return null;

        if (string.IsNullOrEmpty(request.ResourceId))
            return null;

        // 2) Consultar el pago server-side (no confiar en el cuerpo del webhook).
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, $"/v1/payments/{request.ResourceId}");
        Authorize(httpRequest);

        using var response = await _http.SendAsync(httpRequest, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        var payment = await response.Content.ReadFromJsonAsync<PaymentResponse>(cancellationToken: ct);
        if (payment is null || string.IsNullOrEmpty(payment.ExternalReference))
            return null;

        var status = payment.Status switch
        {
            "approved" => PaymentStatus.Approved,
            "rejected" or "cancelled" or "refunded" or "charged_back" => PaymentStatus.Rejected,
            _ => PaymentStatus.Pending,
        };
        return new PaymentNotification(payment.ExternalReference, status);
    }

    private void Authorize(HttpRequestMessage request) =>
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);

    private sealed record PreferenceResponse(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("init_point")] string InitPoint);

    private sealed record PaymentResponse(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("external_reference")] string? ExternalReference);
}
