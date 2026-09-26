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

    /// <summary>Presupuesto diario vigente de cada cuenta de la escuela (fijado por los tutores desde el portal).</summary>
    Task<IReadOnlyList<AccountLimitDto>> GetAccountLimitsAsync(Guid schoolId, CancellationToken ct = default);

    Task<SalesPushResult> PushSalesAsync(Guid schoolId, IReadOnlyList<SaleEntryDto> entries, CancellationToken ct = default);

    /// <summary>Pedidos anticipados ya cobrados en la nube que esta escuela aún no bajó a su libro mayor local.</summary>
    Task<IReadOnlyList<PendingOrderDto>> GetPendingOrdersAsync(Guid schoolId, CancellationToken ct = default);

    /// <summary>Acuse de los pedidos que la escuela ya aplicó localmente: evita que se vuelvan a bajar.</summary>
    Task AckOrdersAsync(Guid schoolId, IReadOnlyList<Guid> orderIds, CancellationToken ct = default);
}
