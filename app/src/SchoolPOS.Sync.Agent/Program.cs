using SchoolPOS.Data;
using SchoolPOS.Data.Sync;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Sync.Agent;

var builder = Host.CreateApplicationBuilder(args);

// Sin esto, arrancado por el Administrador de control de servicios (sc.exe / [Run] del
// instalador) el proceso nunca le reporta "en ejecución": queda en START_PENDING para siempre
// (Windows no lo mata, pero tampoco lo ve como corriendo). Cuando NO corre como servicio (consola,
// dotnet run) esta llamada no hace nada, así que el desarrollo local sigue igual.
builder.Services.AddWindowsService(options => options.ServiceName = "SchoolPOSSync");

// Config de máquina compartida con el POS (la escribe el asistente de primer arranque, o
// Configuración > Sincronización si la llave llegó después): %ProgramData%\LoncherApp, nunca junto
// al .exe (Archivos de programa no es escribible sin admin). reloadOnChange: capturar o rotar la
// llave no requiere reiniciar este servicio.
builder.Configuration.AddJsonFile(SyncAgentConfigFile.FilePath, optional: true, reloadOnChange: true);
// Secretos de desarrollo: dotnet user-secrets (fuera del árbol del proyecto), nunca un
// secrets.json suelto aquí — ese archivo, en el Portal, terminó viajando dentro de un
// `dotnet publish` y filtrando config de desarrollo a producción. No hay razón para arriesgar lo
// mismo aquí solo porque este proceso corre localmente en cada escuela.
if (builder.Environment.IsDevelopment())
    builder.Configuration.AddUserSecrets<Program>();

builder.Services.AddSingleton<IClock, SystemClock>();
// Sin BaseAddress/llave fijos aquí: HttpSyncApiClient los toma de IConfiguration en cada llamada,
// porque pueden no existir todavía cuando el servicio arranca (ver Worker).
builder.Services.AddHttpClient<HttpSyncApiClient>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
