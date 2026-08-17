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
    DateTime RanAtUtc)
{
    public bool HasFailures => TopUpsFailed > 0;

    /// <summary>
    /// Hay consumo esperando a que la nube conozca la cuenta del alumno. No es un error de por sí
    /// (el roster puede ir en camino), pero si no baja con el tiempo indica un roster desfasado.
    /// </summary>
    public bool HasPendingRoster => MovementsSkipped > 0;
}
