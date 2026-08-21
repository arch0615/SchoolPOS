using System.Text.Json;
using System.Text.Json.Serialization;

namespace SchoolPOS.Data.Sync;

/// <summary>
/// Opciones de JSON para <c>/api/sync/*</c>, compartidas entre el portal (servidor) y el Sync
/// Agent (cliente) para que ninguno de los dos lados pueda desalinearse silenciosamente de cómo se
/// codifica <c>MovementType</c> en el cuerpo — como texto ("Sale"), no como un entero mudo.
/// </summary>
public static class SyncJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
