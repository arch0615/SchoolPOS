using FluentAssertions;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Entities;
using SchoolPOS.Payments.MercadoPago;

namespace SchoolPOS.Payments.MercadoPago.Tests;

public class GatewayWebhookSecurityTests
{
    /// <summary>Handler que falla la prueba si se hace cualquier llamada HTTP.</summary>
    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => throw new InvalidOperationException("No debe consultarse el pago cuando la firma es inválida.");
    }

    private sealed class NullStore : ISchoolPaymentAccountStore
    {
        public Task<SchoolPaymentAccount?> GetAsync(Guid schoolId, CancellationToken ct = default)
            => Task.FromResult<SchoolPaymentAccount?>(null);
        public Task SaveAsync(Guid schoolId, string provider, string providerUserId, string accessToken,
            string? refreshToken, DateTime expiresAtUtc, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NullOAuth : IMercadoPagoOAuth
    {
        public string BuildAuthorizationUrl(string state, string redirectUri) => string.Empty;
        public Task<OAuthTokens> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<OAuthTokens> RefreshAsync(string refreshToken, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private static MercadoPagoGateway NewGateway() =>
        new(new HttpClient(new ThrowingHandler()) { BaseAddress = new Uri("https://api.mercadopago.com") },
            new MercadoPagoOptions { AccessToken = "token", WebhookSecret = "secreto" },
            new NullStore(), new NullOAuth());

    [Fact]
    public async Task Invalid_signature_returns_null_and_never_calls_the_api()
    {
        var gateway = NewGateway();

        var result = await gateway.VerifyWebhookAsync(new WebhookRequest(
            RawBody: "{}",
            Signature: "ts=1700000000,v1=deadbeef", // firma falsa
            RequestId: "req-1",
            ResourceId: "123456"));

        result.Should().BeNull("no se debe confiar en una notificación con firma inválida (NFR-3)");
    }

    [Fact]
    public async Task Missing_signature_returns_null()
    {
        var gateway = NewGateway();

        var result = await gateway.VerifyWebhookAsync(new WebhookRequest(
            RawBody: "{}", Signature: null, RequestId: "req-1", ResourceId: "123456"));

        result.Should().BeNull();
    }
}
