using SchoolPOS.Payments.MercadoPago;

namespace SchoolPOS.Portal.Web.Infrastructure;

/// <summary>
/// OAuth de <b>caja de arena</b> para desarrollo: no contacta a Mercado Pago. La URL de
/// autorización apunta a una página interna que simula el consentimiento, y el intercambio de
/// code devuelve tokens ficticios. Sustituir por <see cref="MercadoPagoOAuth"/> en producción.
/// </summary>
public sealed class SandboxMercadoPagoOAuth : IMercadoPagoOAuth
{
    public string BuildAuthorizationUrl(string state, string redirectUri) =>
        $"/oauth/mercadopago/sandbox?state={Uri.EscapeDataString(state)}&redirect_uri={Uri.EscapeDataString(redirectUri)}";

    public Task<OAuthTokens> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct = default) =>
        Task.FromResult(new OAuthTokens(
            AccessToken: $"SBX-AT-{code}",
            RefreshToken: $"SBX-RT-{code}",
            ProviderUserId: "sbx-seller-user",
            ExpiresAtUtc: DateTime.UtcNow.AddHours(6)));

    public Task<OAuthTokens> RefreshAsync(string refreshToken, CancellationToken ct = default) =>
        Task.FromResult(new OAuthTokens("SBX-AT-refreshed", refreshToken, "sbx-seller-user", DateTime.UtcNow.AddHours(6)));
}
