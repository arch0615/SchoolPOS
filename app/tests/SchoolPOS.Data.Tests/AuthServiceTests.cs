using FluentAssertions;
using SchoolPOS.Data.Security;
using SchoolPOS.Data.Services;
using SchoolPOS.Data.Tests.TestSupport;
using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Data.Tests;

public class AuthServiceTests
{
    private static AuthService NewService(TestDatabase db) =>
        new(db.Context, new Pbkdf2PasswordHasher(), new TestClock());

    [Fact]
    public void Password_hash_roundtrips_and_rejects_wrong_password()
    {
        var hasher = new Pbkdf2PasswordHasher();
        var hash = hasher.Hash("Sup3r$ecret");

        hash.Should().StartWith("pbkdf2$");
        hash.Should().NotContain("Sup3r$ecret");
        hasher.Verify("Sup3r$ecret", hash).Should().BeTrue();
        hasher.Verify("wrong", hash).Should().BeFalse();
    }

    [Fact]
    public async Task Authenticate_succeeds_with_correct_credentials_and_returns_role()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var svc = NewService(db);
        await svc.CreateOperatorAsync(school.Id, "cajero1", "clave123", UserRole.Cashier);

        var result = await svc.AuthenticateAsync(school.Id, "cajero1", "clave123");

        result.Succeeded.Should().BeTrue();
        result.User!.Role.Should().Be(UserRole.Cashier);
    }

    [Fact]
    public async Task Authenticate_fails_generically_for_wrong_password()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var svc = NewService(db);
        await svc.CreateOperatorAsync(school.Id, "admin", "correcta", UserRole.Admin);

        var result = await svc.AuthenticateAsync(school.Id, "admin", "incorrecta");

        result.Succeeded.Should().BeFalse();
        result.User.Should().BeNull();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Duplicate_operator_username_is_rejected()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var svc = NewService(db);
        await svc.CreateOperatorAsync(school.Id, "admin", "x", UserRole.Admin);

        var act = () => svc.CreateOperatorAsync(school.Id, "admin", "y", UserRole.Cashier);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
