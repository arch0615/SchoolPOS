using SchoolPOS.Domain.Entities;

namespace SchoolPOS.Domain.Abstractions;

/// <summary>
/// Preferencias de aviso del tutor (FR-WP-10). Sólo persiste la elección; el envío de los avisos
/// se implementa por separado y consulta estas banderas antes de notificar.
/// </summary>
public interface INotificationPreferenceService
{
    /// <summary>
    /// Preferencias del tutor. Si nunca las ha guardado devuelve los valores por omisión sin
    /// escribir en la base: el tutor sólo tiene registro propio cuando decide algo distinto.
    /// </summary>
    Task<GuardianNotificationPreference> GetAsync(Guid guardianId, CancellationToken ct = default);

    /// <summary>Guarda las preferencias del tutor, creando el registro la primera vez.</summary>
    Task SaveAsync(
        Guid guardianId, bool lowBalance, bool topUpConfirmed, bool purchaseMade, bool dailySummary,
        CancellationToken ct = default);
}
