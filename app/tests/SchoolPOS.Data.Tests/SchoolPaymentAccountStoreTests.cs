using FluentAssertions;
using SchoolPOS.Data.Services;
using SchoolPOS.Data.Tests.TestSupport;
using SchoolPOS.Domain.Entities;

namespace SchoolPOS.Data.Tests;

public class SchoolPaymentAccountStoreTests
{
    [Fact]
    public async Task Save_creates_then_upserts_the_account()
    {
        using var db = new TestDatabase();
        var schoolId = Guid.NewGuid();
        var clock = new TestClock();
        var store = new SchoolPaymentAccountStore(db.Context, clock, TestProtector.Create());

        var expiry = clock.UtcNow.AddHours(6);
        await store.SaveAsync(schoolId, "MercadoPago", "user-1", "AT-1", "RT-1", expiry);

        var saved = await store.GetAsync(schoolId);
        saved.Should().NotBeNull();
        saved!.AccessToken.Should().Be("AT-1");
        saved.RefreshToken.Should().Be("RT-1");
        saved.ProviderUserId.Should().Be("user-1");

        // Reconectar (upsert) actualiza el token, no duplica.
        await store.SaveAsync(schoolId, "MercadoPago", "user-1", "AT-2", "RT-2", clock.UtcNow.AddHours(6));
        var count = db.NewContext().SchoolPaymentAccounts.Count(a => a.SchoolId == schoolId);
        count.Should().Be(1);
        (await store.GetAsync(schoolId))!.AccessToken.Should().Be("AT-2");
    }

    [Fact]
    public async Task Get_returns_null_when_not_connected()
    {
        using var db = new TestDatabase();
        var store = new SchoolPaymentAccountStore(db.Context, new TestClock(), TestProtector.Create());
        (await store.GetAsync(Guid.NewGuid())).Should().BeNull();
    }

    /// <summary>
    /// Lo que se guarda en la tabla no debe ser el token. Con muchas escuelas compartiendo la base
    /// de datos de la nube, una sola lectura en claro comprometería a todas a la vez.
    /// </summary>
    [Fact]
    public async Task Tokens_are_not_stored_in_plaintext()
    {
        using var db = new TestDatabase();
        var schoolId = Guid.NewGuid();
        var store = new SchoolPaymentAccountStore(db.Context, new TestClock(), TestProtector.Create());

        await store.SaveAsync(schoolId, "MercadoPago", "user-1", "APP_USR-secreto", "RT-secreto",
            DateTime.UtcNow.AddHours(6));

        var row = db.NewContext().SchoolPaymentAccounts.Single(a => a.SchoolId == schoolId);
        row.AccessToken.Should().NotContain("APP_USR-secreto");
        row.RefreshToken.Should().NotContain("RT-secreto");
        row.AccessToken.Should().StartWith("dp1:", "el prefijo marca el valor como cifrado");
    }

    /// <summary>
    /// Cuentas conectadas antes de que existiera el cifrado: siguen sirviendo (texto plano sin
    /// prefijo) para no obligar a todas las escuelas a reconectar durante la actualización.
    /// </summary>
    [Fact]
    public async Task Legacy_plaintext_token_is_still_readable()
    {
        using var db = new TestDatabase();
        var schoolId = Guid.NewGuid();
        db.Context.SchoolPaymentAccounts.Add(new SchoolPaymentAccount
        {
            SchoolId = schoolId,
            Provider = "MercadoPago",
            ProviderUserId = "user-legacy",
            AccessToken = "AT-EN-CLARO",     // como lo dejaba la versión anterior
            RefreshToken = "RT-EN-CLARO",
            ExpiresAtUtc = DateTime.UtcNow.AddHours(6),
        });
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var store = new SchoolPaymentAccountStore(db.Context, new TestClock(), TestProtector.Create());
        var account = await store.GetAsync(schoolId);

        account.Should().NotBeNull();
        account!.AccessToken.Should().Be("AT-EN-CLARO");
        account.RefreshToken.Should().Be("RT-EN-CLARO");
    }

    /// <summary>
    /// Anillo de llaves perdido (o rotado sin conservar la llave anterior): el token guardado ya no
    /// se puede descifrar. Debe leerse como "no conectada" — que fuerza un OAuth nuevo — y nunca
    /// reventar el flujo de recarga con una excepción de criptografía.
    /// </summary>
    [Fact]
    public async Task Unreadable_token_reads_as_not_connected()
    {
        using var db = new TestDatabase();
        var schoolId = Guid.NewGuid();

        // Guardado con un anillo de llaves, leído con otro distinto.
        var writer = new SchoolPaymentAccountStore(db.Context, new TestClock(), TestProtector.Create());
        await writer.SaveAsync(schoolId, "MercadoPago", "user-1", "AT-1", "RT-1", DateTime.UtcNow.AddHours(6));
        db.Context.ChangeTracker.Clear();

        var reader = new SchoolPaymentAccountStore(db.Context, new TestClock(), TestProtector.Create());
        (await reader.GetAsync(schoolId)).Should().BeNull();
    }
}
