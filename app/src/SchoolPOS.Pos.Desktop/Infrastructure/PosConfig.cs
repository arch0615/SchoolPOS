using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SchoolPOS.Pos.Desktop.Infrastructure;

/// <summary>
/// Ubicación y escritura de la configuración de la caja. <b>No</b> vive junto al ejecutable: una
/// instalación normal queda en <c>Archivos de programa</c>, que no es escribible por el usuario, y
/// el asistente de primer arranque necesita guardar ahí la escuela y la cadena de conexión. Por eso
/// la configuración de máquina va a <c>%ProgramData%\LoncherApp</c>.
/// </summary>
public static class PosConfig
{
    public const string SqliteProvider = "Sqlite";
    public const string SqlServerProvider = "SqlServer";

    /// <summary>Carpeta de datos de la instalación (configuración + base local en modo una caja).</summary>
    public static string Directory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LoncherApp");

    public static string FilePath => Path.Combine(Directory, "appsettings.json");

    /// <summary>Ruta de la base SQLite cuando la caja es única (sin servidor).</summary>
    public static string SqliteDatabasePath => Path.Combine(Directory, "schoolpos.db");

    public static string SqliteConnectionString => $"Data Source={SqliteDatabasePath}";

    /// <summary>
    /// ¿Ya está configurada esta caja? Si no, el arranque abre el asistente en vez de fallar con
    /// una excepción de conexión que al usuario no le dice nada.
    /// </summary>
    public static bool IsConfigured()
    {
        if (!File.Exists(FilePath))
            return false;
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(FilePath))?.AsObject();
            var schoolId = root?["Pos"]?["SchoolId"]?.GetValue<string>();
            var connection = root?["ConnectionStrings"]?["Local"]?.GetValue<string>();
            return Guid.TryParse(schoolId, out var id) && id != Guid.Empty
                   && !string.IsNullOrWhiteSpace(connection);
        }
        catch (Exception)
        {
            return false; // archivo corrupto: se trata como "sin configurar" y el asistente lo reescribe.
        }
    }

    /// <summary>Escribe la configuración de la caja, creando la carpeta si hace falta.</summary>
    public static void Save(Guid schoolId, string provider, string connectionString)
    {
        System.IO.Directory.CreateDirectory(Directory);

        var json = new JsonObject
        {
            ["Pos"] = new JsonObject { ["SchoolId"] = schoolId.ToString() },
            ["Database"] = new JsonObject { ["Provider"] = provider },
            ["ConnectionStrings"] = new JsonObject { ["Local"] = connectionString },
        };

        File.WriteAllText(FilePath, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
