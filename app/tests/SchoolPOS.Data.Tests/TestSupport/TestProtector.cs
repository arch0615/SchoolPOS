using Microsoft.AspNetCore.DataProtection;
using SchoolPOS.Data.Security;
using SchoolPOS.Domain.Abstractions;

namespace SchoolPOS.Data.Tests.TestSupport;

/// <summary>
/// El protector real sobre un anillo de llaves efímero (en memoria). Se prueba el cifrado de
/// verdad, no un doble: un sustituto que devolviera el texto tal cual dejaría pasar justo el fallo
/// que interesa detectar (tokens guardados en claro).
/// </summary>
internal static class TestProtector
{
    public static ISecretProtector Create() =>
        new DataProtectionSecretProtector(new EphemeralDataProtectionProvider());
}
