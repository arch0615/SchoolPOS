using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using SchoolPOS.Payments.MercadoPago;

namespace SchoolPOS.Payments.MercadoPago.Tests;

public class SignatureVerifierTests
{
    private const string Secret = "clave-secreta-webhook";
    private const string DataId = "1234567890";
    private const string RequestId = "req-abc-123";
    private const string Ts = "1700000000";

    /// <summary>Genera una firma x-signature válida como lo haría Mercado Pago.</summary>
    private static string ValidSignature(string secret = Secret, string dataId = DataId,
        string requestId = RequestId, string ts = Ts)
    {
        var manifest = $"id:{dataId};request-id:{requestId};ts:{ts};";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var v1 = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(manifest))).ToLowerInvariant();
        return $"ts={ts},v1={v1}";
    }

    [Fact]
    public void Valid_signature_passes()
    {
        MercadoPagoSignatureVerifier.Verify(ValidSignature(), RequestId, DataId, Secret).Should().BeTrue();
    }

    [Fact]
    public void Tampered_payment_id_fails()
    {
        // Firma calculada para DataId, pero se presenta otro data.id.
        MercadoPagoSignatureVerifier.Verify(ValidSignature(), RequestId, "9999999999", Secret).Should().BeFalse();
    }

    [Fact]
    public void Wrong_secret_fails()
    {
        MercadoPagoSignatureVerifier.Verify(ValidSignature(), RequestId, DataId, "otra-clave").Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("ts=1700000000")]        // sin v1
    [InlineData("v1=abcdef")]            // sin ts
    [InlineData("basura")]
    public void Malformed_signature_fails(string header)
    {
        MercadoPagoSignatureVerifier.Verify(header, RequestId, DataId, Secret).Should().BeFalse();
    }

    [Fact]
    public void Empty_secret_fails()
    {
        MercadoPagoSignatureVerifier.Verify(ValidSignature(), RequestId, DataId, "").Should().BeFalse();
    }
}
