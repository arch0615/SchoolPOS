using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using SchoolPOS.Domain.Abstractions;

namespace SchoolPOS.Data.Security;

/// <summary>
/// Cifrado de secretos en reposo sobre ASP.NET Data Protection. Los valores cifrados se guardan
/// con el prefijo <c>dp1:</c>; un valor sin prefijo se considera heredado (texto plano de antes de
/// esta protección) y se devuelve tal cual, de modo que la migración es transparente: se vuelve a
/// guardar cifrado en el siguiente <c>SaveAsync</c> (o al refrescar el token).
/// <para>
/// <b>Operación:</b> el anillo de llaves debe persistir entre reinicios; si se pierde, los tokens
/// guardados dejan de poder descifrarse y cada escuela tiene que reconectar su cuenta por OAuth.
/// Configúralo con <c>DataProtection:KeyRingPath</c> (ver deploy/INSTALL.md).
/// </para>
/// </summary>
public sealed class DataProtectionSecretProtector : ISecretProtector
{
    /// <summary>Prefijo de versión: permite distinguir texto plano heredado y rotar el esquema.</summary>
    private const string Prefix = "dp1:";

    private readonly IDataProtector _protector;

    public DataProtectionSecretProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("SchoolPOS.PaymentTokens.v1");
    }

    public string Protect(string plaintext) => Prefix + _protector.Protect(plaintext);

    public string? Unprotect(string protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue))
            return null;

        // Heredado: guardado antes de que existiera el cifrado. Sirve hasta que se reescriba.
        if (!protectedValue.StartsWith(Prefix, StringComparison.Ordinal))
            return protectedValue;

        try
        {
            return _protector.Unprotect(protectedValue[Prefix.Length..]);
        }
        catch (CryptographicException)
        {
            // Llave rotada o anillo perdido: no es recuperable. Null → "no conectado" → re-OAuth.
            return null;
        }
    }
}
