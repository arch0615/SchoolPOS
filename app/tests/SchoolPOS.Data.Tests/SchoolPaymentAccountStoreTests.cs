using FluentAssertions;
using SchoolPOS.Data.Services;
using SchoolPOS.Data.Tests.TestSupport;

namespace SchoolPOS.Data.Tests;

public class SchoolPaymentAccountStoreTests
{
    [Fact]
    public async Task Save_creates_then_upserts_the_account()
    {
        using var db = new TestDatabase();
        var schoolId = Guid.NewGuid();
        var clock = new TestClock();
        var store = new SchoolPaymentAccountStore(db.Context, clock);

        var expiry = clock.UtcNow.AddHours(6);
        await store.SaveAsync(schoolId, "MercadoPago", "user-1", "AT-1", "RT-1", expiry);

        var saved = await store.GetAsync(schoolId);
        saved.Should().NotBeNull();
        saved!.AccessToken.Should().Be("AT-1");
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
        var store = new SchoolPaymentAccountStore(db.Context, new TestClock());
        (await store.GetAsync(Guid.NewGuid())).Should().BeNull();
    }
}
