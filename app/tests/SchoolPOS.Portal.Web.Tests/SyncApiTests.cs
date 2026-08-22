using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolPOS.Data;
using SchoolPOS.Data.Sync;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Entities;
using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Portal.Web.Tests;

/// <summary>
/// Pruebas de integración de <c>/api/sync/*</c> a través del pipeline HTTP real (autenticación,
/// autorización, enrutado) — no del servicio de dominio directamente. Es justo la capa que las
/// pruebas de <c>SyncAgent</c> no tocan: ahí se sustituye <c>ISyncApiClient</c> por un adaptador
/// que llama a <c>ISyncCloudService</c> sin pasar por
/// <see cref="Infrastructure.SyncApiKeyAuthenticationHandler"/> ni por la política de
/// autorización. Aquí sí — una llave equivocada, ausente o revocada tiene que fallar en el punto
/// exacto donde producción la rechazaría.
/// </summary>
public sealed class SyncApiTests : IDisposable
{
    private readonly SyncApiWebApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private async Task<(School School, string Key)> SeedSchoolWithKeyAsync(string schoolName = "Escuela de prueba")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
        var keys = scope.ServiceProvider.GetRequiredService<ISyncApiKeyService>();

        var school = new School { Name = schoolName, Currency = "MXN", CommissionRate = 0.05m };
        db.Schools.Add(school);
        await db.SaveChangesAsync();

        var key = await keys.IssueAsync(school.Id, "Prueba");
        return (school, key);
    }

    private async Task<Account> SeedStudentAccountAsync(Guid schoolId, decimal balance = 0m)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();

        var student = new Student { SchoolId = schoolId, EnrollmentNo = $"A-{Guid.NewGuid():N}".Substring(0, 10), FullName = "Alumno" };
        var account = new Account { StudentId = student.Id, Balance = balance, UpdatedAtUtc = DateTime.UtcNow };
        student.Account = account;
        db.Students.Add(student);
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        return account;
    }

    private HttpClient AuthorizedClient(string key)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return client;
    }

    [Fact]
    public async Task Missing_authorization_header_is_rejected()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/sync/topups/pending");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage-not-a-key")]
    [InlineData("sync_00000000000000000000000000000000.deadbeef")] // formato valido, llave inexistente
    public async Task Invalid_or_unknown_key_is_rejected(string badKey)
    {
        var client = AuthorizedClient(badKey);

        var response = await client.GetAsync("/api/sync/topups/pending");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_cookie_session_cannot_authenticate_to_the_sync_api()
    {
        // Sin cookie de sesion y sin encabezado Authorization: ninguno de los dos esquemas
        // aplica, asi que esto tambien prueba que la politica no cae de vuelta a "cualquier
        // usuario autenticado por cualquier medio" -- solo el esquema de llave sirve aqui.
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/api/sync/topups/pending");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "la politica SyncAgent exige el esquema de llave explicitamente, no acepta la cookie");
    }

    [Fact]
    public async Task Revoked_key_is_rejected_on_its_very_next_request()
    {
        var (school, key) = await SeedSchoolWithKeyAsync();
        var client = AuthorizedClient(key);
        (await client.GetAsync("/api/sync/topups/pending")).StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Services.CreateScope())
        {
            var keys = scope.ServiceProvider.GetRequiredService<ISyncApiKeyService>();
            var row = (await keys.ListAsync(school.Id)).Single();
            await keys.RevokeAsync(row.Id);
        }

        var response = await client.GetAsync("/api/sync/topups/pending");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// El caso que justifica todo el rediseño: la llave de una escuela nunca debe poder ver ni
    /// tocar los datos de otra, sin importar que mande el cliente.
    /// </summary>
    [Fact]
    public async Task A_school_only_sees_its_own_pending_top_ups()
    {
        var (schoolA, keyA) = await SeedSchoolWithKeyAsync("Escuela A");
        var (schoolB, keyB) = await SeedSchoolWithKeyAsync("Escuela B");
        var accountA = await SeedStudentAccountAsync(schoolA.Id);
        var accountB = await SeedStudentAccountAsync(schoolB.Id);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
            db.TopUps.AddRange(
                new TopUp { SchoolId = schoolA.Id, AccountId = accountA.Id, Amount = 100m, GatewayRef = "MP-A", Status = TopUpStatus.Confirmed, CreatedAtUtc = DateTime.UtcNow },
                new TopUp { SchoolId = schoolB.Id, AccountId = accountB.Id, Amount = 200m, GatewayRef = "MP-B", Status = TopUpStatus.Confirmed, CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var responseA = await AuthorizedClient(keyA).GetAsync("/api/sync/topups/pending");
        var topUpsA = await responseA.Content.ReadFromJsonAsync<List<PendingTopUpDto>>(SyncJson.Options);

        topUpsA.Should().ContainSingle();
        topUpsA![0].GatewayRef.Should().Be("MP-A");

        var responseB = await AuthorizedClient(keyB).GetAsync("/api/sync/topups/pending");
        var topUpsB = await responseB.Content.ReadFromJsonAsync<List<PendingTopUpDto>>(SyncJson.Options);
        topUpsB.Should().ContainSingle();
        topUpsB![0].GatewayRef.Should().Be("MP-B");
    }

    /// <summary>
    /// Ataque directo: autenticado como la escuela A, se intenta empujar un movimiento contra una
    /// cuenta que en realidad pertenece a la escuela B. Debe quedar "skipped", nunca aplicado, y
    /// el saldo de B no debe moverse ni un centavo.
    /// </summary>
    [Fact]
    public async Task Consumption_for_a_foreign_account_is_skipped_not_applied()
    {
        var (schoolA, keyA) = await SeedSchoolWithKeyAsync("Escuela A");
        var (schoolB, _) = await SeedSchoolWithKeyAsync("Escuela B");
        var foreignAccount = await SeedStudentAccountAsync(schoolB.Id, balance: 500m);

        var entry = new ConsumptionEntryDto(
            Guid.NewGuid(), foreignAccount.Id, MovementType.Sale, -50m, 450m, "VENTA-ROBADA", null, DateTime.UtcNow);

        var response = await AuthorizedClient(keyA).PostAsJsonAsync(
            "/api/sync/consumption", new List<ConsumptionEntryDto> { entry }, SyncJson.Options);
        var result = await response.Content.ReadFromJsonAsync<ConsumptionPushResult>(SyncJson.Options);

        result!.Applied.Should().BeEmpty();
        result.Skipped.Should().ContainSingle().Which.Should().Be(entry.Id);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
        (await db.Accounts.Where(a => a.Id == foreignAccount.Id).Select(a => a.Balance).SingleAsync())
            .Should().Be(500m, "la venta falsificada no debe tocar el saldo de la otra escuela");
        (await db.BalanceMovements.CountAsync(m => m.AccountId == foreignAccount.Id)).Should().Be(0);
    }

    [Fact]
    public async Task Consumption_for_the_authenticated_schools_own_account_is_applied()
    {
        var (school, key) = await SeedSchoolWithKeyAsync();
        var account = await SeedStudentAccountAsync(school.Id, balance: 100m);

        var entry = new ConsumptionEntryDto(
            Guid.NewGuid(), account.Id, MovementType.Sale, -30m, 70m, "VENTA-1", null, DateTime.UtcNow);

        var response = await AuthorizedClient(key).PostAsJsonAsync(
            "/api/sync/consumption", new List<ConsumptionEntryDto> { entry }, SyncJson.Options);
        var result = await response.Content.ReadFromJsonAsync<ConsumptionPushResult>(SyncJson.Options);

        result!.Applied.Should().ContainSingle().Which.Should().Be(entry.Id);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
        (await db.Accounts.Where(a => a.Id == account.Id).Select(a => a.Balance).SingleAsync()).Should().Be(70m);
    }

    [Fact]
    public async Task Roster_entry_is_created_under_the_authenticated_school()
    {
        var (school, key) = await SeedSchoolWithKeyAsync();
        var entry = new RosterEntryDto(
            Guid.NewGuid(), "A-500", "CARD-500", "Alumno Nuevo", true, DateTime.UtcNow, Guid.NewGuid());

        var response = await AuthorizedClient(key).PostAsJsonAsync(
            "/api/sync/roster", new List<RosterEntryDto> { entry }, SyncJson.Options);
        var result = await response.Content.ReadFromJsonAsync<RosterPushResult>(SyncJson.Options);

        result!.Pushed.Should().Be(1);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
        var student = await db.Students.SingleAsync(s => s.Id == entry.Id);
        student.SchoolId.Should().Be(school.Id, "el servidor asigna la escuela de la llave, no algo que mande el cliente");
        (await db.Accounts.Where(a => a.StudentId == entry.Id).Select(a => a.Balance).SingleAsync())
            .Should().Be(0m);
    }
}
