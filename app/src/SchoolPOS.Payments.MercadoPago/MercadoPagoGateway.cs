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
    private readonly ISchoolPaymentAccountStore _accounts;
    private readonly IMercadoPagoOAuth _oauth;

    public MercadoPagoGateway(
        HttpClient http, MercadoPagoOptions options,
        ISchoolPaymentAccountStore accounts, IMercadoPagoOAuth oauth)
    {
        _http = http;
        _options = options;
        _accounts = accounts;
        _oauth = oauth;
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

        // El pago se crea con el token del VENDEDOR (la escuela); marketplace_fee → proveedor.
        var sellerToken = await ResolveSchoolTokenAsync(intent.SchoolId, ct);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/checkout/preferences")
        {
            Content = JsonContent.Create(body),
        };
        Authorize(request, sellerToken);

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

        // 2) Consultar el pago server-side (no confiar en el cuerpo del webhook). Se usa el token de
        //    la app (marketplace); en algunos casos MP requiere el del vendedor — validar en integración.
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, $"/v1/payments/{request.ResourceId}");
        Authorize(httpRequest, _options.AccessToken);

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

    /// <summary>
    /// Devuelve el access token del vendedor (escuela) conectado por OAuth, refrescándolo si venció.
    /// Si la escuela no ha conectado su cuenta, usa el token de la app como respaldo (modo de una
    /// sola cuenta) o falla si tampoco existe.
    /// </summary>
    private async Task<string> ResolveSchoolTokenAsync(Guid schoolId, CancellationToken ct)
    {
        var account = await _accounts.GetAsync(schoolId, ct);
        if (account is null)
        {
            if (!string.IsNullOrEmpty(_options.AccessToken))
                return _options.AccessToken;
            throw new InvalidOperationException(
                $"La escuela {schoolId} no ha conectado su cuenta de Mercado Pago (OAuth).");
        }

        if (account.ExpiresAtUtc <= DateTime.UtcNow.AddMinutes(1) && !string.IsNullOrEmpty(account.RefreshToken))
        {
            var refreshed = await _oauth.RefreshAsync(account.RefreshToken!, ct);
            await _accounts.SaveAsync(schoolId, "MercadoPago", refreshed.ProviderUserId,
                refreshed.AccessToken, refreshed.RefreshToken, refreshed.ExpiresAtUtc, ct);
            return refreshed.AccessToken;
        }

        return account.AccessToken;
    }

    private static void Authorize(HttpRequestMessage request, string token) =>
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private sealed record PreferenceResponse(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("init_point")] string InitPoint);

    private sealed record PaymentResponse(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("external_reference")] string? ExternalReference);
}
