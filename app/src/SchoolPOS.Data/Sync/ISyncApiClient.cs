namespace SchoolPOS.Data.Sync;

/// <summary>
/// Lo que <see cref="SyncAgent"/> necesita de "la nube", visto desde una sola escuela — sin
/// SchoolId explícito, porque de qué escuela se trata ya lo decide la llave con la que se
/// autenticó, nunca un valor que el propio agente declare. La implementación real
/// (<c>HttpSyncApiClient</c>, en <c>SchoolPOS.Sync.Agent</c>) llama a <c>/api/sync/*</c>; las
/// pruebas usan un adaptador delgado sobre <see cref="ISyncCloudService"/> para ejercitar el mismo
/// flujo de control sin levantar un servidor HTTP.
/// </summary>
public interface ISyncApiClient
{
    Task<IReadOnlyList<PendingTopUpDto>> GetPendingTopUpsAsync(CancellationToken ct = default);

    Task AckTopUpsAsync(IReadOnlyList<Guid> topUpIds, CancellationToken ct = default);

    Task<RosterPushResult> PushRosterAsync(IReadOnlyList<RosterEntryDto> entries, CancellationToken ct = default);

    Task<ConsumptionPushResult> PushConsumptionAsync(IReadOnlyList<ConsumptionEntryDto> entries, CancellationToken ct = default);
}
