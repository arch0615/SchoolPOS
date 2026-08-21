using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using SchoolPOS.Data;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Enums;
using SchoolPOS.Payments.MercadoPago;
using SchoolPOS.Invoicing.Sw;
using SchoolPOS.Portal.Web.Infrastructure;
using SchoolPOS.Portal.Web.Infrastructure.Email;

var builder = WebApplication.CreateBuilder(args);

// secrets.json: overrides locales fuera de control de versiones (ya está en .gitignore). Se
// aplica después de appsettings.json/appsettings.{Environment}.json, así que gana sobre ambos;
// optional=true para que su ausencia no rompa un clon nuevo del repo sin credenciales todavía.
builder.Configuration.AddJsonFile("secrets.json", optional: true, reloadOnChange: true);
var config = builder.Configuration;

var provider = config["Database:Provider"] ?? "Sqlite";
var connectionString = config.GetConnectionString("Portal") ?? "Data Source=schoolpos-portal.db";
// Solo para sembrar datos de demostración en desarrollo. El portal atiende a TODAS las escuelas:
// la escuela de cada tutor sale de su sesión, nunca de esta configuración.
var demoSchoolId = config.GetValue<Guid>("Portal:SchoolId");

// DB local/nube + servicios de dominio (proveedor configurable).
builder.Services.AddSchoolPosData(options =>
{
    if (string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
        options.UseSqlServer(connectionString);
    else
        options.UseSqlite(connectionString, o => o.MigrationsAssembly(SqliteMigrations.AssemblyName));
});

// Anillo de llaves de Data Protection (cifra los tokens OAuth de las escuelas). Debe persistir
// entre reinicios y compartirse entre instancias: si se pierde, cada escuela tiene que reconectar
// su cuenta de Mercado Pago. Por defecto va al perfil del usuario, que en un contenedor o bajo un
// servicio con perfil efímero se borra — de ahí que en producción se configure una ruta explícita.
var keyRingPath = config["DataProtection:KeyRingPath"];
if (!string.IsNullOrWhiteSpace(keyRingPath))
{
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath))
        .SetApplicationName("SchoolPOS.Portal");
}

builder.Services.AddScoped<SchoolDirectory>();

// Pasarela de pago: sandbox (desarrollo) o Mercado Pago real (producción), según configuración.
var paymentsProvider = config["Payments:Provider"] ?? "Sandbox";
if (string.Equals(paymentsProvider, "MercadoPago", StringComparison.OrdinalIgnoreCase))
{
    var mpOptions = config.GetSection("MercadoPago").Get<MercadoPagoOptions>() ?? new MercadoPagoOptions();
    builder.Services.AddSingleton(mpOptions);
    builder.Services.AddHttpClient<IPaymentGateway, MercadoPagoGateway>(client =>
    {
        if (!string.IsNullOrEmpty(mpOptions.BaseUrl))
            client.BaseAddress = new Uri(mpOptions.BaseUrl);
    });
}
else
{
    builder.Services.AddScoped<IPaymentGateway, SandboxPaymentGateway>();
}

builder.Services.AddSingleton(new PortalOptions
{
    VendorAccessCode = config["Portal:VendorAccessCode"] ?? "vendor-demo",
});

// OAuth marketplace: sandbox (dev) o Mercado Pago real (cuando Payments:Provider = MercadoPago).
builder.Services.AddSingleton(new OnboardingStateProtector(
    config["MercadoPago:StateSecret"] ?? "dev-onboarding-state-secret"));
if (string.Equals(paymentsProvider, "MercadoPago", StringComparison.OrdinalIgnoreCase))
{
    var oauthOptions = config.GetSection("MercadoPagoOAuth").Get<MercadoPagoOAuthOptions>() ?? new MercadoPagoOAuthOptions();
    builder.Services.AddSingleton(oauthOptions);
    builder.Services.AddHttpClient<IMercadoPagoOAuth, MercadoPagoOAuth>(client =>
    {
        if (!string.IsNullOrEmpty(oauthOptions.ApiBaseUrl))
            client.BaseAddress = new Uri(oauthOptions.ApiBaseUrl);
    });
}
else
{
    builder.Services.AddSingleton<IMercadoPagoOAuth, SandboxMercadoPagoOAuth>();
}

// Correo transaccional: SMTP real o emisor de log (desarrollo).
var emailProvider = config["Email:Provider"] ?? "Log";
if (string.Equals(emailProvider, "Smtp", StringComparison.OrdinalIgnoreCase))
{
    var smtp = config.GetSection("Smtp").Get<SmtpOptions>() ?? new SmtpOptions();
    builder.Services.AddSingleton(smtp);
    builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
}
else
{
    builder.Services.AddSingleton<IEmailSender, LoggingEmailSender>();
}

// CFDI de comisión: emisor simulado (Null, por defecto en dev) o SW Sapien real.
var cfdiSettings = config.GetSection("Cfdi").Get<CfdiSettings>();
if (cfdiSettings is not null)
    builder.Services.AddSingleton(cfdiSettings);
if (string.Equals(config["Cfdi:Provider"], "Sw", StringComparison.OrdinalIgnoreCase))
{
    var swOptions = config.GetSection("SwCfdi").Get<SwCfdiOptions>() ?? new SwCfdiOptions();
    builder.Services.AddSingleton(swOptions);
    builder.Services.AddHttpClient<ICfdiIssuer, SwCfdiIssuer>(client =>
    {
        if (!string.IsNullOrEmpty(swOptions.BaseUrl))
            client.BaseAddress = new Uri(swOptions.BaseUrl);
    });
}
// else: NullCfdiIssuer ya está registrado por AddSchoolPosData (default de desarrollo).

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Error";
        options.ExpireTimeSpan = TimeSpan.FromHours(4);
        options.SlidingExpiration = true;
        // Autenticado pero sin permiso → página de error 403 de marca (con el código en la query).
        options.Events.OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.Redirect("/Error?code=403");
            return Task.CompletedTask;
        };
        // Sin sesión → cada área tiene su propio login (padre/proveedor/tienda), no solo /Account/Login.
        options.Events.OnRedirectToLogin = ctx =>
        {
            var loginPath = ctx.Request.Path.StartsWithSegments("/Vendor") ? "/Vendor/Login"
                : ctx.Request.Path.StartsWithSegments("/School") ? "/School/Login"
                : "/Account/Login";
            var returnUrl = ctx.Request.Path + ctx.Request.QueryString;
            ctx.Response.Redirect(QueryHelpers.AddQueryString(loginPath, "ReturnUrl", returnUrl));
            return Task.CompletedTask;
        };
    })
    // Esquema separado para el Sync Agent: una llave por escuela (Authorization: Bearer), nunca
    // la cookie de sesión. El esquema por defecto sigue siendo la cookie — este solo se activa en
    // los endpoints que lo piden explícitamente (política "SyncAgent").
    .AddScheme<AuthenticationSchemeOptions, SyncApiKeyAuthenticationHandler>(
        SyncApiKeyAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization(options =>
{
    // Portal del tutor: sesión autenticada que NO es de proveedor ni de tienda (esas llevan
    // portal_role). Evita que esas dos sesiones truene (GetGuardianId) o vean páginas ajenas.
    options.AddPolicy("Guardian", policy => policy.RequireAssertion(ctx =>
        ctx.User.Identity?.IsAuthenticated == true
        && !ctx.User.HasClaim(c => c.Type == ClaimsExtensions.PortalRoleClaim)));
    // Panel del proveedor: requiere identidad de proveedor (comisiones vendor-wide).
    options.AddPolicy("Vendor", policy => policy.RequireClaim(ClaimsExtensions.PortalRoleClaim, "vendor"));
    // Panel de la tienda escolar: operador del POS, limitado a su propia escuela.
    // RBAC de la tienda — refleja los permisos del POS de escritorio (PosSession):
    //   School          cualquier operador activo (venta)         → Cajero/Almacén/Administrador
    //   SchoolInventory manejo de inventario                      → Almacén/Administrador
    //   SchoolAdmin     reportes y panel de administración        → Administrador
    options.AddPolicy("School", policy => policy.RequireClaim(ClaimsExtensions.PortalRoleClaim, "school"));
    options.AddPolicy("SchoolInventory", policy => policy
        .RequireClaim(ClaimsExtensions.PortalRoleClaim, "school")
        .RequireRole(nameof(UserRole.Warehouse), nameof(UserRole.Admin)));
    options.AddPolicy("SchoolAdmin", policy => policy
        .RequireClaim(ClaimsExtensions.PortalRoleClaim, "school")
        .RequireRole(nameof(UserRole.Admin)));
    // Sync Agent de una escuela: solo el esquema de llave, nunca la cookie de sesión de un
    // navegador — un tutor o un operador con sesión abierta no debe poder llamar /api/sync/*.
    options.AddPolicy("SyncAgent", policy => policy
        .AddAuthenticationSchemes(SyncApiKeyAuthenticationHandler.SchemeName)
        .RequireAuthenticatedUser());
});
builder.Services.AddRazorPages();
// /api/sync/* usa las mismas opciones de JSON (MovementType como texto) que el cliente del Sync
// Agent — SyncJson.Options es la única fuente de verdad para ese contrato, no una copia local.
// Solo afecta a los endpoints con Results.Ok/parámetros de minimal API, no a Razor Pages.
builder.Services.ConfigureHttpJsonOptions(o =>
{
    foreach (var converter in SchoolPOS.Data.Sync.SyncJson.Options.Converters)
        o.SerializerOptions.Converters.Add(converter);
});

var app = builder.Build();

// Inicialización de base de datos + datos de demostración (desarrollo).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
    // Migraciones en ambos proveedores: SQLite tiene su propio juego, así que una base ya creada
    // sí puede recibir cambios de esquema.
    await db.Database.MigrateAsync();

    if (config.GetValue<bool>("Portal:SeedDemoData"))
        await DemoDataSeeder.SeedAsync(db, demoSchoolId);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseStatusCodePagesWithReExecute("/Error", "?code={0}");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();
app.MapSyncEndpoints();

// Webhook de la pasarela: confirma el pago server-side (NUNCA por la redirección del navegador,
// NFR-3) y aplica la recarga al libro mayor de forma idempotente.
app.MapPost("/api/payments/webhook", async (
    HttpRequest request, IPaymentGateway gateway, ITopUpService topUps, SchoolDbContext db,
    IEmailSender emailSender, INotificationPreferenceService notificationPrefs,
    ILogger<Program> webhookLogger, CancellationToken ct) =>
{
    try
    {
        using var reader = new StreamReader(request.Body);
        var payload = await reader.ReadToEndAsync(ct);

        // Mercado Pago envía x-signature + x-request-id y el id del pago en la query (data.id).
        var webhook = new WebhookRequest(
            RawBody: payload,
            Signature: request.Headers["x-signature"].ToString(),
            RequestId: request.Headers["x-request-id"].ToString(),
            ResourceId: request.Query["data.id"].FirstOrDefault() ?? request.Query["id"].FirstOrDefault());

        var notification = await gateway.VerifyWebhookAsync(webhook, ct);
        if (notification is null)
            return Results.BadRequest("Notificación inválida.");

        if (notification.Status == PaymentStatus.Approved)
        {
            var topUp = await topUps.ConfirmAsync(notification.GatewayRef, ct);
            await topUps.ApplyConfirmedAsync(topUp.Id, ct);
            await TopUpNotifier.SendConfirmedAsync(db, emailSender, notificationPrefs, webhookLogger, topUp.Id, ct);
        }
        return Results.Ok();
    }
    catch (Exception ex)
    {
        // 500, no 200: le dice a Mercado Pago que reintente la entrega — nunca confirmar
        // recepción de un pago que no se pudo aplicar de verdad (NFR-3).
        webhookLogger.LogError(ex, "Fallo al procesar el webhook de pagos.");
        return Results.Problem("No se pudo procesar la notificación.", statusCode: 500);
    }
});

// ---- Onboarding OAuth marketplace (la escuela conecta su cuenta de Mercado Pago) ----
app.MapGet("/oauth/mercadopago/start", (
    HttpContext http, Guid schoolId, IMercadoPagoOAuth oauth, OnboardingStateProtector protector,
    ILogger<Program> oauthLogger) =>
{
    try
    {
        var state = protector.Protect(schoolId, TimeSpan.FromMinutes(15));
        var redirectUri = $"{http.Request.Scheme}://{http.Request.Host}/oauth/mercadopago/callback";
        return Results.Redirect(oauth.BuildAuthorizationUrl(state, redirectUri));
    }
    catch (Exception ex)
    {
        oauthLogger.LogError(ex, "No se pudo iniciar la conexión con Mercado Pago (schoolId {SchoolId}).", schoolId);
        return Results.Redirect("/Vendor/Connections?error=1");
    }
}).RequireAuthorization("Vendor");

app.MapGet("/oauth/mercadopago/callback", async (
    HttpContext http, string? code, string? state,
    IMercadoPagoOAuth oauth, ISchoolPaymentAccountStore store, OnboardingStateProtector protector,
    ILogger<Program> oauthLogger, CancellationToken ct) =>
{
    // El state firmado prueba que el flujo lo inició nuestro portal (CSRF) y trae el schoolId.
    if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state) || !protector.TryUnprotect(state, out var schoolId))
        return Results.Redirect("/Vendor/Connections?error=1");

    try
    {
        var redirectUri = $"{http.Request.Scheme}://{http.Request.Host}/oauth/mercadopago/callback";
        var tokens = await oauth.ExchangeCodeAsync(code, redirectUri, ct);
        await store.SaveAsync(schoolId, "MercadoPago", tokens.ProviderUserId,
            tokens.AccessToken, tokens.RefreshToken, tokens.ExpiresAtUtc, ct);
        return Results.Redirect("/Vendor/Connections?connected=1");
    }
    catch (Exception ex)
    {
        oauthLogger.LogError(ex, "No se pudo completar la conexión con Mercado Pago (schoolId {SchoolId}).", schoolId);
        return Results.Redirect("/Vendor/Connections?error=1");
    }
});

// Página de consentimiento simulada (solo desarrollo — reemplaza al checkout real de Mercado Pago).
app.MapGet("/oauth/mercadopago/sandbox", (string state, string redirect_uri) =>
{
    var authorize = $"{redirect_uri}?code=SBX-CODE&state={Uri.EscapeDataString(state)}";
    var html = $$"""
        <!doctype html><html lang="es"><head><meta charset="utf-8"><title>Conectar Mercado Pago (simulación)</title></head>
        <body style="font-family:system-ui;max-width:460px;margin:60px auto;text-align:center">
          <h1>Mercado Pago (simulación)</h1>
          <p>La escuela autoriza al proveedor a crear pagos a su nombre con split de comisión.</p>
          <p><a href="{{authorize}}" style="background:#009ee3;color:#fff;padding:12px 20px;border-radius:6px;text-decoration:none">Autorizar</a></p>
        </body></html>
        """;
    return Results.Content(html, "text/html");
});

app.Run();
