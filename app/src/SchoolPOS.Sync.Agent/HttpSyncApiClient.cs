using System.Net.Http.Json;
using SchoolPOS.Data.Sync;

namespace SchoolPOS.Sync.Agent;

/// <summary>
/// Implementación real de <see cref="ISyncApiClient"/>: habla con <c>/api/sync/*</c> por HTTPS,
/// autenticada con la llave de esta escuela (<c>Sync:ApiKey</c>). Nunca tiene una cadena de
/// conexión a la base de datos de la nube — eso es justo lo que esta clase reemplaza.
/// </summary>
public sealed class HttpSyncApiClient : ISyncApiClient
{
    private readonly HttpClient _http;

    public HttpSyncApiClient(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<PendingTopUpDto>> GetPendingTopUpsAsync(CancellationToken ct = default)
    {
        var result = await _http.GetFromJsonAsync<List<PendingTopUpDto>>(
            "/api/sync/topups/pending", SyncJson.Options, ct);
        return result ?? new List<PendingTopUpDto>();
    }

    public async Task AckTopUpsAsync(IReadOnlyList<Guid> topUpIds, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync(
            "/api/sync/topups/ack", new AckTopUpsRequest(topUpIds.ToList()), SyncJson.Options, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<RosterPushResult> PushRosterAsync(
        IReadOnlyList<RosterEntryDto> entries, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/sync/roster", entries, SyncJson.Options, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RosterPushResult>(SyncJson.Options, ct))!;
    }

    public async Task<ConsumptionPushResult> PushConsumptionAsync(
        IReadOnlyList<ConsumptionEntryDto> entries, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/sync/consumption", entries, SyncJson.Options, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ConsumptionPushResult>(SyncJson.Options, ct))!;
    }
}
