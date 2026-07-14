using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using SchoolPOS.Data;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Portal.Web.Infrastructure;

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

// Pasarela de pago (sandbox en desarrollo; sustituir por Mercado Pago real en producción).
builder.Services.AddScoped<IPaymentGateway, SandboxPaymentGateway>();
builder.Services.AddSingleton(new PortalOptions { SchoolId = schoolId });

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/Login";
        options.ExpireTimeSpan = TimeSpan.FromHours(4);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();
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
    var signature = request.Headers["X-Signature"].ToString();

    var notification = await gateway.VerifyWebhookAsync(signature, payload, ct);
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
