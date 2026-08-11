using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchoolPOS.Data;
using SchoolPOS.Domain.Abstractions;

namespace SchoolPOS.Portal.Web.Infrastructure.Email;

/// <summary>
/// Envía el aviso de "recarga confirmada" a los tutores vinculados al alumno que la recibió,
/// respetando su preferencia <see cref="Domain.Entities.GuardianNotificationPreference.TopUpConfirmed"/>.
/// Se invoca justo después de aplicar la recarga (webhook real y checkout de simulación) — nunca
/// antes: el dinero se acredita sin depender del correo. Nunca lanza: cada tutor se envía en su
/// propio try/catch, así un fallo de SMTP para uno no oculta ni bloquea el aviso a los demás.
/// </summary>
public static class TopUpNotifier
{
    public static async Task SendConfirmedAsync(
        SchoolDbContext db, IEmailSender email, INotificationPreferenceService prefs,
        ILogger logger, Guid topUpId, CancellationToken ct = default)
    {
        var topUp = await db.TopUps.AsNoTracking()
            .Include(t => t.Account).ThenInclude(a => a.Student)
            .FirstOrDefaultAsync(t => t.Id == topUpId, ct);
        if (topUp is null)
            return;

        var student = topUp.Account.Student;
        var guardianIds = await db.GuardianStudents.AsNoTracking()
            .Where(gs => gs.StudentId == student.Id)
            .Select(gs => gs.GuardianId)
            .ToListAsync(ct);

        foreach (var guardianId in guardianIds)
        {
            try
            {
                var pref = await prefs.GetAsync(guardianId, ct);
                if (!pref.TopUpConfirmed)
                    continue;

                var guardian = await db.Guardians.AsNoTracking()
                    .FirstOrDefaultAsync(g => g.Id == guardianId, ct);
                if (guardian is null)
                    continue;

                var body =
                    $"<p>Hola {WebUtility.HtmlEncode(guardian.FullName)},</p>" +
                    $"<p>Confirmamos tu recarga de <strong>{topUp.Amount:C2}</strong> " +
                    $"para <strong>{WebUtility.HtmlEncode(student.FullName)}</strong>. " +
                    "El saldo ya está disponible para usarse en la tienda escolar.</p>";
                await email.SendAsync(guardian.Email, "Recarga confirmada", body, ct);
            }
            catch (Exception ex)
            {
                // Un aviso fallido nunca debe revertir ni ocultar que la recarga sí se aplicó.
                logger.LogWarning(ex, "No se pudo enviar el aviso de recarga confirmada a {GuardianId}", guardianId);
            }
        }
    }
}
