using SchoolPOS.Data.Services;
using SchoolPOS.Data.Sync;
using SchoolPOS.Domain.Abstractions;

namespace SchoolPOS.Data.Tests.TestSupport;

/// <summary>
/// Adaptador de pruebas: implementa <see cref="ISyncApiClient"/> (lo que ve <see cref="SyncAgent"/>
/// desde una escuela) delegando al mismo <see cref="ISyncCloudService"/> que usan los endpoints
/// reales de <c>/api/sync/*</c>, con un SchoolId fijo en vez de uno resuelto de una llave HTTP. Así
/// las pruebas ejercitan la lógica real del servidor sin levantar un servidor.
/// </summary>
public sealed class SingleSchoolSyncApiClient : ISyncApiClient
{
    private readonly ISyncCloudService _cloud;
    private readonly Guid _schoolId;

    public SingleSchoolSyncApiClient(SchoolDbContext cloudContext, IClock clock, Guid schoolId)
    {
        _cloud = new SyncCloudService(cloudContext, clock);
        _schoolId = schoolId;
    }

    public Task<IReadOnlyList<PendingTopUpDto>> GetPendingTopUpsAsync(CancellationToken ct = default) =>
        _cloud.GetPendingTopUpsAsync(_schoolId, ct);

    public Task AckTopUpsAsync(IReadOnlyList<Guid> topUpIds, CancellationToken ct = default) =>
        _cloud.AckTopUpsAsync(_schoolId, topUpIds, ct);

    public Task<RosterPushResult> PushRosterAsync(IReadOnlyList<RosterEntryDto> entries, CancellationToken ct = default) =>
        _cloud.PushRosterAsync(_schoolId, entries, ct);

    public Task<ConsumptionPushResult> PushConsumptionAsync(IReadOnlyList<ConsumptionEntryDto> entries, CancellationToken ct = default) =>
        _cloud.PushConsumptionAsync(_schoolId, entries, ct);
}
