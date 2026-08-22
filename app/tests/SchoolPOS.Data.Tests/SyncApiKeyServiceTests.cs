using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SchoolPOS.Data.Security;
using SchoolPOS.Data.Services;
using SchoolPOS.Data.Tests.TestSupport;

namespace SchoolPOS.Data.Tests;

/// <summary>
/// Llaves de <c>/api/sync/*</c>: es la única credencial que un Sync Agent tiene desde que dejó de
/// abrir una segunda conexión a la base de datos, así que un fallo aquí (una llave revocada que
/// sigue entrando, o una de otra escuela que verifica) sería la puerta de todo el aislamiento entre
/// escuelas.
/// </summary>
public class SyncApiKeyServiceTests
{
    private static SyncApiKeyService NewService(TestDatabase db) =>
        new(db.Context, new Pbkdf2PasswordHasher(), new TestClock());

    [Fact]
    public async Task Issued_key_verifies_back_to_its_own_school()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var svc = NewService(db);

        var key = await svc.IssueAsync(school.Id, "Agente principal");

        key.Should().StartWith("sync_");
        (await svc.VerifyAsync(key)).Should().Be(school.Id);
    }

    [Fact]
    public async Task Only_the_hash_is_persisted_not_the_plaintext_key()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var key = await NewService(db).IssueAsync(school.Id, "Agente");

        var row = db.NewContext().SyncApiKeys.Single(k => k.SchoolId == school.Id);
        row.SecretHash.Should().NotBe(key);
        row.SecretHash.Should().StartWith("pbkdf2$", "mismo formato que las contraseñas, NFR-6");
    }

    [Fact]
    public async Task Tampered_secret_does_not_verify()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var svc = NewService(db);
        var key = await svc.IssueAsync(school.Id, "Agente");

        var tampered = key[..^1] + (key[^1] == '0' ? '1' : '0');

        (await svc.VerifyAsync(tampered)).Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-even-close")]
    [InlineData("sync_no-dot-here")]
    [InlineData("bearer_sync_wrongprefix.secret")]
    public async Task Malformed_keys_are_rejected_without_touching_the_database(string malformed)
    {
        using var db = new TestDatabase();
        var svc = NewService(db);

        (await svc.VerifyAsync(malformed)).Should().BeNull();
    }

    [Fact]
    public async Task A_key_from_one_school_never_verifies_as_another()
    {
        using var db = new TestDatabase();
        var schoolA = db.SeedSchool();
        var schoolB = new SchoolPOS.Domain.Entities.School { Name = "Otra escuela", Currency = "MXN" };
        db.Context.Schools.Add(schoolB);
        await db.Context.SaveChangesAsync();
        var svc = NewService(db);

        var keyA = await svc.IssueAsync(schoolA.Id, "A");
        var keyB = await svc.IssueAsync(schoolB.Id, "B");

        (await svc.VerifyAsync(keyA)).Should().Be(schoolA.Id);
        (await svc.VerifyAsync(keyB)).Should().Be(schoolB.Id);
        (await svc.VerifyAsync(keyA)).Should().NotBe(schoolB.Id);
    }

    /// <summary>
    /// El caso que de verdad importa para revocar un dispositivo comprometido: la llave deja de
    /// servir de inmediato, no en la próxima ventana de tiempo ni tras algún caché.
    /// </summary>
    [Fact]
    public async Task Revoked_key_stops_verifying_immediately()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var svc = NewService(db);
        var key = await svc.IssueAsync(school.Id, "Agente");
        (await svc.VerifyAsync(key)).Should().Be(school.Id);

        var keyId = db.NewContext().SyncApiKeys.Single(k => k.SchoolId == school.Id).Id;
        await svc.RevokeAsync(keyId);

        (await svc.VerifyAsync(key)).Should().BeNull();
    }

    [Fact]
    public async Task Revoking_twice_is_harmless()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var svc = NewService(db);
        await svc.IssueAsync(school.Id, "Agente");
        var keyId = db.NewContext().SyncApiKeys.Single(k => k.SchoolId == school.Id).Id;

        await svc.RevokeAsync(keyId);
        var act = () => svc.RevokeAsync(keyId);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Successful_verification_stamps_last_used()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var clock = new TestClock();
        var svc = new SyncApiKeyService(db.Context, new Pbkdf2PasswordHasher(), clock);
        var key = await svc.IssueAsync(school.Id, "Agente");

        var before = db.NewContext().SyncApiKeys.Single(k => k.SchoolId == school.Id);
        before.LastUsedAtUtc.Should().BeNull();

        await svc.VerifyAsync(key);

        var after = db.NewContext().SyncApiKeys.Single(k => k.SchoolId == school.Id);
        after.LastUsedAtUtc.Should().Be(clock.UtcNow);
    }

    [Fact]
    public async Task List_only_returns_that_schools_keys_most_recent_first()
    {
        using var db = new TestDatabase();
        var schoolA = db.SeedSchool();
        var schoolB = new SchoolPOS.Domain.Entities.School { Name = "Otra escuela", Currency = "MXN" };
        db.Context.Schools.Add(schoolB);
        await db.Context.SaveChangesAsync();
        var clock = new TestClock();
        var svc = new SyncApiKeyService(db.Context, new Pbkdf2PasswordHasher(), clock);

        await svc.IssueAsync(schoolA.Id, "Primera");
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        await svc.IssueAsync(schoolB.Id, "De la otra escuela");
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        await svc.IssueAsync(schoolA.Id, "Segunda");

        var keys = await svc.ListAsync(schoolA.Id);
        keys.Should().HaveCount(2);
        keys.Select(k => k.Label).Should().Equal("Segunda", "Primera");
    }
}
