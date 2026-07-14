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
    DateTime RanAtUtc)
{
    public bool HasFailures => TopUpsFailed > 0;
}
