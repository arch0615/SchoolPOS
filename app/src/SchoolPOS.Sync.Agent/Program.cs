using SchoolPOS.Data;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Sync.Agent;

var builder = Host.CreateApplicationBuilder(args);

// secrets.json: la llave real de esta escuela vive aquí, gitignored, fuera de appsettings.json.
builder.Configuration.AddJsonFile("secrets.json", optional: true, reloadOnChange: true);
var config = builder.Configuration;

builder.Services.AddSingleton<IClock, SystemClock>();

var apiBaseUrl = config["Sync:ApiBaseUrl"]
    ?? throw new InvalidOperationException("Falta Sync:ApiBaseUrl (p. ej. https://loncherapp.com).");
var apiKey = config["Sync:ApiKey"]
    ?? throw new InvalidOperationException("Falta Sync:ApiKey — la llave de esta escuela (ver Vendor > Escuelas > Llaves).");
builder.Services.AddHttpClient<HttpSyncApiClient>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
    client.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
});

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
