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

    /// <summary>Presupuesto diario vigente de cada cuenta de esta escuela (fijado por los tutores desde el portal).</summary>
    Task<IReadOnlyList<AccountLimitDto>> GetAccountLimitsAsync(CancellationToken ct = default);

    Task<SalesPushResult> PushSalesAsync(IReadOnlyList<SaleEntryDto> entries, CancellationToken ct = default);

    /// <summary>Pedidos anticipados ya cobrados en la nube que esta escuela aún no bajó a su libro mayor local.</summary>
    Task<IReadOnlyList<PendingOrderDto>> GetPendingOrdersAsync(CancellationToken ct = default);

    /// <summary>Acuse de los pedidos que la escuela ya aplicó localmente: evita que se vuelvan a bajar.</summary>
    Task AckOrdersAsync(IReadOnlyList<Guid> orderIds, CancellationToken ct = default);
}
