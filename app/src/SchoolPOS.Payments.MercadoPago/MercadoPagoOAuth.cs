using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace SchoolPOS.Payments.MercadoPago;

/// <summary>Configuración OAuth marketplace de Mercado Pago (credenciales de la app del proveedor).</summary>
public sealed class MercadoPagoOAuthOptions
{
    public string ClientId { get; set; } = string.Empty;      // = App ID
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Host del endpoint de autorización (varía por país, p. ej. .com.mx).</summary>
    public string AuthBaseUrl { get; set; } = "https://auth.mercadopago.com.mx";

    /// <summary>Host de la API (token endpoint).</summary>
    public string ApiBaseUrl { get; set; } = "https://api.mercadopago.com";
}

/// <summary>Tokens del vendedor (escuela) obtenidos por OAuth.</summary>
public sealed record OAuthTokens(string AccessToken, string? RefreshToken, string ProviderUserId, DateTime ExpiresAtUtc);

/// <summary>
/// Flujo OAuth marketplace de Mercado Pago: cada escuela (vendedor) autoriza la app del proveedor
/// para que éste cree pagos a su nombre con <c>marketplace_fee</c> (split de comisión).
/// </summary>
public interface IMercadoPagoOAuth
{
    /// <summary>URL a la que se redirige a la escuela para autorizar la conexión.</summary>
    string BuildAuthorizationUrl(string state, string redirectUri);

    /// <summary>Intercambia el <c>code</c> del callback por los tokens del vendedor.</summary>
    Task<OAuthTokens> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct = default);

    /// <summary>Refresca el access token con el refresh token.</summary>
    Task<OAuthTokens> RefreshAsync(string refreshToken, CancellationToken ct = default);
}

/// <summary>Implementación real del OAuth de Mercado Pago (HTTP a <c>/oauth/token</c>).</summary>
public sealed class MercadoPagoOAuth : IMercadoPagoOAuth
{
    private readonly HttpClient _http;
    private readonly MercadoPagoOAuthOptions _options;

    public MercadoPagoOAuth(HttpClient http, MercadoPagoOAuthOptions options)
    {
        _http = http;
        _options = options;
        if (_http.BaseAddress is null && !string.IsNullOrEmpty(_options.ApiBaseUrl))
            _http.BaseAddress = new Uri(_options.ApiBaseUrl);
    }

    public string BuildAuthorizationUrl(string state, string redirectUri) =>
        $"{_options.AuthBaseUrl}/authorization" +
        $"?client_id={Uri.EscapeDataString(_options.ClientId)}" +
        "&response_type=code&platform_id=mp" +
        $"&state={Uri.EscapeDataString(state)}" +
        $"&redirect_uri={Uri.EscapeDataString(redirectUri)}";

    public Task<OAuthTokens> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct = default) =>
        PostTokenAsync(new
        {
            client_id = _options.ClientId,
            client_secret = _options.ClientSecret,
            grant_type = "authorization_code",
            code,
            redirect_uri = redirectUri,
        }, ct);

    public Task<OAuthTokens> RefreshAsync(string refreshToken, CancellationToken ct = default) =>
        PostTokenAsync(new
        {
            client_id = _options.ClientId,
            client_secret = _options.ClientSecret,
            grant_type = "refresh_token",
            refresh_token = refreshToken,
        }, ct);

    private async Task<OAuthTokens> PostTokenAsync(object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/oauth/token")
        {
            Content = JsonContent.Create(body),
        };
        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Mercado Pago: respuesta OAuth vacía.");

        return new OAuthTokens(
            token.AccessToken ?? throw new InvalidOperationException("Mercado Pago: sin access_token."),
            token.RefreshToken,
            token.UserId?.ToString() ?? string.Empty,
            DateTime.UtcNow.AddSeconds(token.ExpiresIn > 0 ? token.ExpiresIn : 21600));
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("user_id")] long? UserId,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
