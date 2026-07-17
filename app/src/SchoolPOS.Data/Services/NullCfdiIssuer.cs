using SchoolPOS.Domain.Abstractions;

namespace SchoolPOS.Data.Services;

/// <summary>
/// Emisor de CFDI <b>simulado</b> para desarrollo/pruebas: NO contacta al PAC ni genera documentos
/// fiscales reales. Devuelve un UUID determinista para poder ejercitar el flujo completo
/// (calcular comisión → emitir → persistir) sin riesgo fiscal. Sustituir por el emisor SW real
/// en producción (`Cfdi:Provider = Sw`).
/// </summary>
public sealed class NullCfdiIssuer : ICfdiIssuer
{
    public Task<CfdiResult> IssueAsync(CommissionInvoiceRequest request, CancellationToken ct = default)
    {
        // UUID determinista a partir de los datos (no es un folio fiscal real).
        var seed = $"{request.Receiver.Rfc}|{request.PeriodFromUtc:O}|{request.PeriodToUtc:O}|{request.Amount}";
        var uuid = DeterministicGuid(seed).ToString();
        return Task.FromResult(CfdiResult.Ok(uuid, xml: "<!-- CFDI simulado (desarrollo) -->"));
    }

    private static Guid DeterministicGuid(string input)
    {
        var bytes = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return new Guid(bytes);
    }
}
