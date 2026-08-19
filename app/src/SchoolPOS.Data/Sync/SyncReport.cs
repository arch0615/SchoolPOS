namespace SchoolPOS.Data.Sync;

/// <summary>
/// Estado de una corrida de sincronización (salud/observabilidad, 3.19). Los contadores permiten
/// detectar fallas y reintentos.
/// </summary>
public sealed record SyncReport(
    int TopUpsPulled,
    int TopUpsApplied,
    int TopUpsFailed,
    int MovementsPushed,
    int MovementsSkipped,
    int RosterPushed,
    DateTime RanAtUtc)
{
    public bool HasFailures => TopUpsFailed > 0;

    /// <summary>
    /// Hay consumo esperando a que la nube conozca la cuenta del alumno. Con el padrón subiendo en
    /// la misma corrida (antes del consumo) esto ya no debería sostenerse de una corrida a la
    /// siguiente; si lo hace, indica un roster genuinamente desfasado (p. ej. una fila en conflicto
    /// que <see cref="RosterPushed"/> no logra reconciliar).
    /// </summary>
    public bool HasPendingRoster => MovementsSkipped > 0;
}
