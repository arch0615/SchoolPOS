using System.Security.Claims;
using SchoolPOS.Data.Sync;

namespace SchoolPOS.Portal.Web.Infrastructure;

/// <summary>
/// Reemplaza el acceso directo a la base de datos que antes usaba el Sync Agent (una cadena de
/// conexión "Cloud" por escuela) por endpoints autenticados con la llave de esa escuela. Cada
/// escuela solo puede tocar su propio padrón, sus propias recargas y su propio saldo — nunca los
/// de otra — porque el <c>schoolId</c> sale siempre de la llave verificada
/// (<see cref="SyncApiKeyAuthenticationHandler"/>), nunca de algo que mande el cliente.
/// </summary>
public static class SyncEndpoints
{
    public static void MapSyncEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/sync").RequireAuthorization("SyncAgent");

        // Recargas confirmadas en la nube que la escuela aún no bajó a su libro mayor local.
        group.MapGet("/topups/pending", async (ClaimsPrincipal user, ISyncCloudService sync, CancellationToken ct) =>
        {
            var items = await sync.GetPendingTopUpsAsync(user.GetSchoolId(), ct);
            return Results.Ok(items);
        });

        // Acuse de las recargas que la escuela ya aplicó localmente: evita que se vuelvan a bajar.
        group.MapPost("/topups/ack", async (
            ClaimsPrincipal user, AckTopUpsRequest body, ISyncCloudService sync, CancellationToken ct) =>
        {
            await sync.AckTopUpsAsync(user.GetSchoolId(), body.TopUpIds, ct);
            return Results.Ok();
        });

        // Padrón (alumnos + cuenta) que nació en el POS de la escuela.
        group.MapPost("/roster", async (
            ClaimsPrincipal user, List<RosterEntryDto> body, ISyncCloudService sync, CancellationToken ct) =>
        {
            var result = await sync.PushRosterAsync(user.GetSchoolId(), body, ct);
            return Results.Ok(result);
        });

        // Consumo (ventas/devoluciones/ajustes/recargas en efectivo) que nació en la escuela.
        group.MapPost("/consumption", async (
            ClaimsPrincipal user, List<ConsumptionEntryDto> body, ISyncCloudService sync, CancellationToken ct) =>
        {
            var result = await sync.PushConsumptionAsync(user.GetSchoolId(), body, ct);
            return Results.Ok(result);
        });
    }
}
