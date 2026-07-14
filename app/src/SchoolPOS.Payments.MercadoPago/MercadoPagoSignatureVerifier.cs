using System.Security.Cryptography;
using System.Text;

namespace SchoolPOS.Payments.MercadoPago;

/// <summary>
/// Validación de la firma del webhook de Mercado Pago (NFR-3: confirmar server-side, nunca por la
/// redirección del navegador). El encabezado <c>x-signature</c> tiene el formato
/// <c>ts=&lt;timestamp&gt;,v1=&lt;hmac_hex&gt;</c>. El manifiesto a firmar con HMAC-SHA256 y la clave
/// secreta es <c>id:&lt;data.id&gt;;request-id:&lt;x-request-id&gt;;ts:&lt;ts&gt;;</c> (se omiten los
/// segmentos ausentes). La comparación es en tiempo constante. Es una función pura, testeable.
/// </summary>
public static class MercadoPagoSignatureVerifier
{
    public static bool Verify(string? xSignature, string? xRequestId, string? dataId, string secret)
    {
        if (string.IsNullOrWhiteSpace(xSignature) || string.IsNullOrWhiteSpace(secret))
            return false;

        var (ts, v1) = ParseSignature(xSignature);
        if (ts is null || v1 is null)
            return false;

        var manifest = new StringBuilder();
        if (!string.IsNullOrEmpty(dataId)) manifest.Append("id:").Append(dataId).Append(';');
        if (!string.IsNullOrEmpty(xRequestId)) manifest.Append("request-id:").Append(xRequestId).Append(';');
        manifest.Append("ts:").Append(ts).Append(';');

        var computed = ComputeHmacHex(manifest.ToString(), secret);
        return FixedTimeEqualsHex(computed, v1);
    }

    private static (string? Ts, string? V1) ParseSignature(string header)
    {
        string? ts = null, v1 = null;
        foreach (var part in header.Split(','))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2) continue;
            var key = kv[0].Trim();
            var value = kv[1].Trim();
            if (key == "ts") ts = value;
            else if (key == "v1") v1 = value;
        }
        return (ts, v1);
    }

    private static string ComputeHmacHex(string message, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool FixedTimeEqualsHex(string computedHex, string providedHex)
    {
        // Comparación en tiempo constante sobre bytes (evita fugas por temporización).
        byte[] a, b;
        try
        {
            a = Convert.FromHexString(computedHex);
            b = Convert.FromHexString(providedHex);
        }
        catch (FormatException)
        {
            return false;
        }
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
