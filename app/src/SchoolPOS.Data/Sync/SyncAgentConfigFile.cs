using System.Text.Json;
using System.Text.Json.Nodes;

namespace SchoolPOS.Data.Sync;

/// <summary>
/// Config de máquina del Agente de Sincronización, compartida con el POS: el asistente de primer
/// arranque (o la pantalla Configuración &gt; Sincronización) la escribe, y el propio agente la lee.
/// Vive en <c>%ProgramData%\LoncherApp</c>, igual que la config del POS (<c>PosConfig</c>) — nunca
/// junto al .exe del agente, que normalmente queda en Archivos de programa y no es escribible sin
/// admin. Así, capturar o rotar la llave de sincronización después no requiere reinstalar el
/// servicio: el agente recarga este archivo solo (<c>reloadOnChange</c>) y retoma en el siguiente
/// ciclo.
/// </summary>
public static class SyncAgentConfigFile
{
    /// <summary>Único portal de producción: no hay un despliegue distinto por escuela.</summary>
    public const string DefaultApiBaseUrl = "https://loncherapp.com";

    public static string Directory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LoncherApp");

    public static string FilePath => Path.Combine(Directory, "sync-agent.settings.json");

    public sealed record Settings(string Provider, string ConnectionString, string ApiBaseUrl, string? ApiKey);

    public static Settings? Read()
    {
        if (!File.Exists(FilePath))
            return null;
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(FilePath))?.AsObject();
            var provider = root?["Database"]?["Provider"]?.GetValue<string>();
            var connectionString = root?["ConnectionStrings"]?["Local"]?.GetValue<string>();
            var apiBaseUrl = root?["Sync"]?["ApiBaseUrl"]?.GetValue<string>();
            var apiKey = root?["Sync"]?["ApiKey"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(connectionString)
                || string.IsNullOrWhiteSpace(apiBaseUrl))
                return null;
            return new Settings(provider, connectionString, apiBaseUrl,
                string.IsNullOrWhiteSpace(apiKey) ? null : apiKey);
        }
        catch (Exception)
        {
            return null; // archivo corrupto: se trata como "sin configurar".
        }
    }

    /// <summary>Escribe la configuración completa (la usa el asistente de primer arranque).</summary>
    public static void Save(string provider, string connectionString, string apiBaseUrl, string? apiKey)
    {
        System.IO.Directory.CreateDirectory(Directory);

        var json = new JsonObject
        {
            ["Database"] = new JsonObject { ["Provider"] = provider },
            ["ConnectionStrings"] = new JsonObject { ["Local"] = connectionString },
            ["Sync"] = new JsonObject { ["ApiBaseUrl"] = apiBaseUrl, ["ApiKey"] = apiKey ?? string.Empty },
        };

        File.WriteAllText(FilePath, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
