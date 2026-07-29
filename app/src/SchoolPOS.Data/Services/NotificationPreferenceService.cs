using Microsoft.EntityFrameworkCore;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Entities;

namespace SchoolPOS.Data.Services;

/// <summary>
/// Preferencias de aviso del tutor (FR-WP-10). Se guardan de forma perezosa: mientras el tutor no
/// cambie nada no existe fila y se devuelven los valores por omisión de la entidad.
/// </summary>
public sealed class NotificationPreferenceService : INotificationPreferenceService
{
    private readonly SchoolDbContext _db;
    private readonly IClock _clock;

    public NotificationPreferenceService(SchoolDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<GuardianNotificationPreference> GetAsync(Guid guardianId, CancellationToken ct = default)
    {
        var existing = await _db.GuardianNotificationPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.GuardianId == guardianId, ct);

        return existing ?? new GuardianNotificationPreference { GuardianId = guardianId };
    }

    public async Task SaveAsync(
        Guid guardianId, bool lowBalance, bool topUpConfirmed, bool purchaseMade, bool dailySummary,
        CancellationToken ct = default)
    {
        var prefs = await _db.GuardianNotificationPreferences
            .FirstOrDefaultAsync(p => p.GuardianId == guardianId, ct);

        if (prefs is null)
        {
            prefs = new GuardianNotificationPreference { GuardianId = guardianId };
            _db.GuardianNotificationPreferences.Add(prefs);
        }

        prefs.LowBalance = lowBalance;
        prefs.TopUpConfirmed = topUpConfirmed;
        prefs.PurchaseMade = purchaseMade;
        prefs.DailySummary = dailySummary;
        prefs.UpdatedAtUtc = _clock.UtcNow;

        await _db.SaveChangesAsync(ct);
    }
}
