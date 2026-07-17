using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using SchoolPOS.Domain.Abstractions;

namespace SchoolPOS.Invoicing.Sw;

/// <summary>
/// Emisor de CFDI real vía <b>SW Sapien</b> (PAC). Autentica, arma el CFDI 4.0 y lo timbra (SW
/// sella con el CSD del emisor cargado en su cuenta). Devuelve el UUID (folio fiscal) y el XML.
///
/// ⚠️ IMPORTANTE: genera <b>documentos fiscales reales</b>. Usar primero contra el <i>sandbox</i>
/// de SW (BaseUrl de pruebas) y VALIDAR con el contador los catálogos SAT (ClaveProdServ, régimen,
/// IVA, uso CFDI) y el esquema/endpoints exactos contra la documentación vigente de SW antes de
/// producción. Los nombres de campos y rutas aquí son una base y deben confirmarse.
/// </summary>
public sealed class SwCfdiIssuer : ICfdiIssuer
{
    private readonly HttpClient _http;
    private readonly SwCfdiOptions _options;

    public SwCfdiIssuer(HttpClient http, SwCfdiOptions options)
    {
        _http = http;
        _options = options;
        if (_http.BaseAddress is null && !string.IsNullOrEmpty(_options.BaseUrl))
            _http.BaseAddress = new Uri(_options.BaseUrl);
    }

    public async Task<CfdiResult> IssueAsync(CommissionInvoiceRequest request, CancellationToken ct = default)
    {
        try
        {
            var token = await GetTokenAsync(ct);
            var payload = BuildCfdi(request);

            // TODO(SW): validar la ruta y el esquema exactos contra la documentación vigente de SW.
            using var http = new HttpRequestMessage(HttpMethod.Post, "/v4/cfdi40/issue/json/v4")
            {
                Content = JsonContent.Create(payload),
            };
            http.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await _http.SendAsync(http, ct);
            var stamp = await response.Content.ReadFromJsonAsync<SwStampResponse>(cancellationToken: ct);

            if (!response.IsSuccessStatusCode || stamp is null ||
                !string.Equals(stamp.Status, "success", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrEmpty(stamp.Data?.Uuid))
            {
                return CfdiResult.Fail(stamp?.Message ?? $"SW error HTTP {(int)response.StatusCode}");
            }

            return CfdiResult.Ok(stamp.Data!.Uuid!, stamp.Data.Cfdi);
        }
        catch (Exception ex)
        {
            return CfdiResult.Fail($"Error al timbrar con SW: {ex.Message}");
        }
    }

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(_options.Token))
            return _options.Token!;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v2/security/authenticate")
        {
            Content = JsonContent.Create(new { user = _options.User, password = _options.Password }),
        };
        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadFromJsonAsync<SwAuthResponse>(cancellationToken: ct);
        return auth?.Data?.Token
            ?? throw new InvalidOperationException("SW: no se obtuvo token de autenticación.");
    }

    /// <summary>Arma el objeto CFDI 4.0 para el timbrado. Los importes salen de la comisión + IVA.</summary>
    private object BuildCfdi(CommissionInvoiceRequest r)
    {
        var (subtotal, iva, total) = ComputeAmounts(r.Amount, r.TaxRate, r.TaxInclusive);
        var hasTax = r.TaxRate > 0m;

        return new
        {
            Emisor = new { Rfc = _options.IssuerRfc, Nombre = _options.IssuerName, RegimenFiscal = _options.IssuerTaxRegime },
            Receptor = new
            {
                Rfc = r.Receiver.Rfc,
                Nombre = r.Receiver.Name,
                DomicilioFiscalReceptor = r.Receiver.PostalCode,
                RegimenFiscalReceptor = r.Receiver.TaxRegime,
                UsoCFDI = r.Receiver.CfdiUse,
            },
            Moneda = r.Currency,
            FormaPago = _options.PaymentForm,
            MetodoPago = _options.PaymentMethod,
            LugarExpedicion = _options.IssuerPostalCode,
            SubTotal = subtotal,
            Total = total,
            Conceptos = new[]
            {
                new
                {
                    ClaveProdServ = _options.ConceptSatKey,
                    ClaveUnidad = _options.ConceptUnitKey,
                    Cantidad = 1,
                    Descripcion = r.Concept,
                    ValorUnitario = subtotal,
                    Importe = subtotal,
                    ObjetoImp = hasTax ? "02" : "01",
                    Impuestos = hasTax
                        ? new
                        {
                            Traslados = new[]
                            {
                                new
                                {
                                    Base = subtotal, Impuesto = "002", TipoFactor = "Tasa",
                                    TasaOCuota = r.TaxRate, Importe = iva,
                                },
                            },
                        }
                        : null,
                },
            },
        };
    }

    private static (decimal SubTotal, decimal Iva, decimal Total) ComputeAmounts(
        decimal amount, decimal taxRate, bool taxInclusive)
    {
        amount = Round(amount);
        if (taxRate <= 0m)
            return (amount, 0m, amount);

        if (taxInclusive)
        {
            var baseAmount = Round(amount / (1m + taxRate));
            var iva = amount - baseAmount; // el total permanece igual a la comisión
            return (baseAmount, Round(iva), amount);
        }

        var tax = Round(amount * taxRate);
        return (amount, tax, amount + tax);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    // ---- Respuestas de SW (validar nombres contra la documentación) ----
    private sealed record SwAuthResponse(
        [property: JsonPropertyName("data")] SwAuthData? Data,
        [property: JsonPropertyName("status")] string? Status);

    private sealed record SwAuthData([property: JsonPropertyName("token")] string? Token);

    private sealed record SwStampResponse(
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("data")] SwStampData? Data);

    private sealed record SwStampData(
        [property: JsonPropertyName("uuid")] string? Uuid,
        [property: JsonPropertyName("cfdi")] string? Cfdi);
}
