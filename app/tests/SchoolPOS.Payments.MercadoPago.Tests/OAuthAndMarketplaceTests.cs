using System.Net;
using System.Text;
using FluentAssertions;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Entities;
using SchoolPOS.Payments.MercadoPago;

namespace SchoolPOS.Payments.MercadoPago.Tests;

public class OAuthAndMarketplaceTests
{
    /// <summary>Handler que devuelve una respuesta fija y captura la última petición.</summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly string _json;
        public string? LastAuthorization { get; private set; }
        public string? LastBody { get; private set; }

        public CapturingHandler(string json) => _json = json;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastAuthorization = request.Headers.Authorization?.ToString();
            if (request.Content is not null)
                LastBody = await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class FixedStore : ISchoolPaymentAccountStore
    {
        private readonly SchoolPaymentAccount? _account;
        public FixedStore(SchoolPaymentAccount? account) => _account = account;
        public Task<SchoolPaymentAccount?> GetAsync(Guid schoolId, CancellationToken ct = default)
            => Task.FromResult(_account);
        public Task SaveAsync(Guid schoolId, string provider, string providerUserId, string accessToken,
            string? refreshToken, DateTime expiresAtUtc, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class UnusedOAuth : IMercadoPagoOAuth
    {
        public string BuildAuthorizationUrl(string state, string redirectUri) => string.Empty;
        public Task<OAuthTokens> ExchangeCodeAsync(string c, string r, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OAuthTokens> RefreshAsync(string r, CancellationToken ct = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task ExchangeCode_parses_seller_tokens()
    {
        var handler = new CapturingHandler(
            """{"access_token":"AT-seller","refresh_token":"RT-seller","user_id":12345,"expires_in":3600}""");
        var oauth = new MercadoPagoOAuth(new HttpClient(handler) { BaseAddress = new Uri("https://api.mercadopago.com") },
            new MercadoPagoOAuthOptions { ClientId = "app", ClientSecret = "secret" });

        var tokens = await oauth.ExchangeCodeAsync("the-code", "https://portal/callback");

        tokens.AccessToken.Should().Be("AT-seller");
        tokens.RefreshToken.Should().Be("RT-seller");
        tokens.ProviderUserId.Should().Be("12345");
        tokens.ExpiresAtUtc.Should().BeAfter(DateTime.UtcNow);
        handler.LastBody.Should().Contain("authorization_code").And.Contain("the-code");
    }

    [Fact]
    public async Task CreatePreference_uses_the_connected_school_seller_token()
    {
        var schoolId = Guid.NewGuid();
        var account = new SchoolPaymentAccount
        {
            SchoolId = schoolId,
            Provider = "MercadoPago",
            AccessToken = "SELLER-TOKEN",
            ExpiresAtUtc = DateTime.UtcNow.AddHours(2), // vigente → sin refresh
        };
        var handler = new CapturingHandler("""{"id":"pref1","init_point":"https://mp/checkout/pref1"}""");
        var gateway = new MercadoPagoGateway(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.mercadopago.com") },
            new MercadoPagoOptions { AccessToken = "APP-TOKEN" },
            new FixedStore(account), new UnusedOAuth());

        var pref = await gateway.CreatePreferenceAsync(new PaymentIntent(
            Guid.NewGuid(), schoolId, 100m, 5m, "MXN", "Recarga"));

        pref.CheckoutUrl.Should().Be("https://mp/checkout/pref1");
        handler.LastAuthorization.Should().Be("Bearer SELLER-TOKEN", "el pago se crea con el token del vendedor");
        handler.LastBody.Should().Contain("marketplace_fee");
    }

    [Fact]
    public async Task CreatePreference_falls_back_to_app_token_when_school_not_connected()
    {
        var handler = new CapturingHandler("""{"id":"pref1","init_point":"https://mp/checkout/pref1"}""");
        var gateway = new MercadoPagoGateway(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.mercadopago.com") },
            new MercadoPagoOptions { AccessToken = "APP-TOKEN" },
            new FixedStore(null), new UnusedOAuth());

        await gateway.CreatePreferenceAsync(new PaymentIntent(Guid.NewGuid(), Guid.NewGuid(), 100m, 5m, "MXN", "Recarga"));

        handler.LastAuthorization.Should().Be("Bearer APP-TOKEN");
    }
}
