using FluentAssertions;
using SchoolPOS.Data.Security;
using SchoolPOS.Data.Services;
using SchoolPOS.Data.Tests.TestSupport;

namespace SchoolPOS.Data.Tests;

public class PasswordRecoveryTests
{
    private static GuardianService NewService(TestDatabase db, TestClock clock) =>
        new(db.Context, new Pbkdf2PasswordHasher(), clock);

    [Fact]
    public async Task Request_then_reset_lets_user_login_with_new_password()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var clock = new TestClock();
        var svc = NewService(db, clock);
        await svc.RegisterAsync(school.Id, "p@c.com", "vieja123", "P");

        var token = await svc.RequestPasswordResetAsync(school.Id, "P@C.com");
        token.Should().NotBeNullOrEmpty();

        var ok = await svc.ResetPasswordAsync(school.Id, "p@c.com", token!, "nueva123");
        ok.Should().BeTrue();

        (await svc.AuthenticateAsync(school.Id, "p@c.com", "nueva123")).Succeeded.Should().BeTrue();
        (await svc.AuthenticateAsync(school.Id, "p@c.com", "vieja123")).Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Request_for_unknown_email_returns_null_without_revealing()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var svc = NewService(db, new TestClock());

        (await svc.RequestPasswordResetAsync(school.Id, "noexiste@c.com")).Should().BeNull();
    }

    [Fact]
    public async Task Reset_with_wrong_token_fails()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var svc = NewService(db, new TestClock());
        await svc.RegisterAsync(school.Id, "p@c.com", "vieja123", "P");
        await svc.RequestPasswordResetAsync(school.Id, "p@c.com");

        (await svc.ResetPasswordAsync(school.Id, "p@c.com", "TOKEN-FALSO", "nueva123")).Should().BeFalse();
    }

    [Fact]
    public async Task Reset_token_is_single_use()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var svc = NewService(db, new TestClock());
        await svc.RegisterAsync(school.Id, "p@c.com", "vieja123", "P");
        var token = await svc.RequestPasswordResetAsync(school.Id, "p@c.com");

        (await svc.ResetPasswordAsync(school.Id, "p@c.com", token!, "nueva123")).Should().BeTrue();
        (await svc.ResetPasswordAsync(school.Id, "p@c.com", token!, "otra1234")).Should().BeFalse("el token es de un solo uso");
    }

    [Fact]
    public async Task Reset_with_expired_token_fails()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var clock = new TestClock();
        var svc = NewService(db, clock);
        await svc.RegisterAsync(school.Id, "p@c.com", "vieja123", "P");
        var token = await svc.RequestPasswordResetAsync(school.Id, "p@c.com");

        clock.UtcNow = clock.UtcNow.AddHours(2); // el token caduca en 1 hora

        (await svc.ResetPasswordAsync(school.Id, "p@c.com", token!, "nueva123")).Should().BeFalse();
    }

    [Fact]
    public async Task Change_password_requires_correct_current()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var svc = NewService(db, new TestClock());
        var guardian = await svc.RegisterAsync(school.Id, "p@c.com", "actual123", "P");

        (await svc.ChangePasswordAsync(guardian.Id, "incorrecta", "nueva123")).Should().BeFalse();
        (await svc.ChangePasswordAsync(guardian.Id, "actual123", "nueva123")).Should().BeTrue();
        (await svc.AuthenticateAsync(school.Id, "p@c.com", "nueva123")).Succeeded.Should().BeTrue();
    }
}
