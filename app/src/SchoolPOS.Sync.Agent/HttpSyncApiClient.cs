using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using SchoolPOS.Data.Sync;

namespace SchoolPOS.Sync.Agent;

/// <summary>
/// Implementación real de <see cref="ISyncApiClient"/>: habla con <c>/api/sync/*</c> por HTTPS,
/// autenticada con la llave de esta escuela (<c>Sync:ApiKey</c>). Nunca tiene una cadena de
/// conexión a la base de datos de la nube — eso es justo lo que esta clase reemplaza.
/// La URL y la llave se leen de <see cref="IConfiguration"/> en cada llamada (no una sola vez al
/// construirse): la llave puede llegar después, capturada desde Configuración &gt; Sincronización
/// del POS mientras este servicio ya está corriendo, y <c>reloadOnChange</c> la recarga sola.
/// </summary>
public sealed class HttpSyncApiClient : ISyncApiClient
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;

    public HttpSyncApiClient(HttpClient http, IConfiguration config)
    {
        _http = http;
        _config = config;
    }

    private void ApplyCurrentConfig()
    {
        var baseUrl = _config["Sync:ApiBaseUrl"];
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            var uri = new Uri(baseUrl);
            if (_http.BaseAddress != uri)
                _http.BaseAddress = uri;
        }
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _config["Sync:ApiKey"] ?? string.Empty);
    }

    public async Task<IReadOnlyList<PendingTopUpDto>> GetPendingTopUpsAsync(CancellationToken ct = default)
    {
        ApplyCurrentConfig();
        var result = await _http.GetFromJsonAsync<List<PendingTopUpDto>>(
            "/api/sync/topups/pending", SyncJson.Options, ct);
        return result ?? new List<PendingTopUpDto>();
    }

    public async Task AckTopUpsAsync(IReadOnlyList<Guid> topUpIds, CancellationToken ct = default)
    {
        ApplyCurrentConfig();
        var response = await _http.PostAsJsonAsync(
            "/api/sync/topups/ack", new AckTopUpsRequest(topUpIds.ToList()), SyncJson.Options, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<RosterPushResult> PushRosterAsync(
        IReadOnlyList<RosterEntryDto> entries, CancellationToken ct = default)
    {
        ApplyCurrentConfig();
        var response = await _http.PostAsJsonAsync("/api/sync/roster", entries, SyncJson.Options, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RosterPushResult>(SyncJson.Options, ct))!;
    }

    public async Task<ConsumptionPushResult> PushConsumptionAsync(
        IReadOnlyList<ConsumptionEntryDto> entries, CancellationToken ct = default)
    {
        ApplyCurrentConfig();
        var response = await _http.PostAsJsonAsync("/api/sync/consumption", entries, SyncJson.Options, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ConsumptionPushResult>(SyncJson.Options, ct))!;
    }
}
