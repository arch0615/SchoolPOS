using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using SchoolPOS.Data;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Payments.MercadoPago;
using SchoolPOS.Invoicing.Sw;
using SchoolPOS.Portal.Web.Infrastructure;
using SchoolPOS.Portal.Web.Infrastructure.Email;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

var provider = config["Database:Provider"] ?? "Sqlite";
var connectionString = config.GetConnectionString("Portal") ?? "Data Source=schoolpos-portal.db";
var schoolId = config.GetValue<Guid>("Portal:SchoolId");

// DB local/nube + servicios de dominio (proveedor configurable).
builder.Services.AddSchoolPosData(options =>
{
    if (string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
        options.UseSqlServer(connectionString);
    else
        options.UseSqlite(connectionString);
});

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
    SchoolId = schoolId,
    VendorAccessCode = config["Portal:VendorAccessCode"] ?? "vendor-demo",
});

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
        options.AccessDeniedPath = "/Account/Login";
        options.ExpireTimeSpan = TimeSpan.FromHours(4);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization(options =>
{
    // Panel del proveedor: requiere identidad de proveedor (comisiones vendor-wide).
    options.AddPolicy("Vendor", policy => policy.RequireClaim(ClaimsExtensions.PortalRoleClaim, "vendor"));
});
builder.Services.AddRazorPages();

var app = builder.Build();

// Inicialización de base de datos + datos de demostración (desarrollo).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
    if (string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
        await db.Database.MigrateAsync();
    else
        await db.Database.EnsureCreatedAsync();

    if (config.GetValue<bool>("Portal:SeedDemoData"))
        await DemoDataSeeder.SeedAsync(db, schoolId);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();

// Webhook de la pasarela: confirma el pago server-side (NUNCA por la redirección del navegador,
// NFR-3) y aplica la recarga al libro mayor de forma idempotente.
app.MapPost("/api/payments/webhook", async (
    HttpRequest request, IPaymentGateway gateway, ITopUpService topUps, CancellationToken ct) =>
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
    }
    return Results.Ok();
});

app.Run();
