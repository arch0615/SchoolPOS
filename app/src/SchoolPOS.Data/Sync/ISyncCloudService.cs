namespace SchoolPOS.Data.Sync;

/// <summary>
/// Lado servidor de la sincronización nube↔escuela, expuesto por <c>/api/sync/*</c>. Es el mismo
/// trabajo que antes hacía <see cref="SyncAgent"/> abriendo un segundo <c>SchoolDbContext</c>
/// apuntado a la nube; ahora corre del lado del portal y el Sync Agent lo invoca por HTTP en vez
/// de tener una cadena de conexión a la base de datos. Cada método confía únicamente en el
/// <c>schoolId</c> de la llave autenticada, nunca en un SchoolId que mande el cliente en el
/// cuerpo — así una escuela no puede, ni por error ni a propósito, tocar el padrón o el saldo de
/// otra.
/// </summary>
public interface ISyncCloudService
{
    Task<IReadOnlyList<PendingTopUpDto>> GetPendingTopUpsAsync(Guid schoolId, CancellationToken ct = default);

    Task AckTopUpsAsync(Guid schoolId, IReadOnlyList<Guid> topUpIds, CancellationToken ct = default);

    Task<RosterPushResult> PushRosterAsync(Guid schoolId, IReadOnlyList<RosterEntryDto> entries, CancellationToken ct = default);

    Task<ConsumptionPushResult> PushConsumptionAsync(Guid schoolId, IReadOnlyList<ConsumptionEntryDto> entries, CancellationToken ct = default);
}
