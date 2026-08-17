using FluentAssertions;
using SchoolPOS.Data.Security;
using SchoolPOS.Data.Services;
using SchoolPOS.Data.Tests.TestSupport;
using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Data.Tests;

public class AuthServiceTests
{
    private static AuthService NewService(TestDatabase db, TestClock? clock = null) =>
        new(db.Context, new Pbkdf2PasswordHasher(), clock ?? new TestClock());

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

    /// <summary>
    /// Bloqueo tras 5 intentos fallidos (NFR-6). La misma cuenta de operador abre la consola web de
    /// la tienda, así que sin bloqueo la contraseña del cajero queda expuesta a fuerza bruta desde
    /// internet, no solo desde la LAN de la escuela.
    /// </summary>
    [Fact]
    public async Task Account_locks_after_five_failed_attempts()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var svc = NewService(db);
        await svc.CreateOperatorAsync(school.Id, "admin", "correcta", UserRole.Admin);

        for (var i = 0; i < 4; i++)
        {
            var attempt = await svc.AuthenticateAsync(school.Id, "admin", "incorrecta");
            attempt.IsLockedOut.Should().BeFalse($"aún quedan intentos (intento {i + 1})");
        }

        var fifth = await svc.AuthenticateAsync(school.Id, "admin", "incorrecta");
        fifth.IsLockedOut.Should().BeTrue();

        // Bloqueada: ni siquiera la contraseña correcta entra.
        var correct = await svc.AuthenticateAsync(school.Id, "admin", "correcta");
        correct.Succeeded.Should().BeFalse();
        correct.IsLockedOut.Should().BeTrue();
    }

    [Fact]
    public async Task Lockout_expires_after_the_window()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var clock = new TestClock();
        var svc = NewService(db, clock);
        await svc.CreateOperatorAsync(school.Id, "admin", "correcta", UserRole.Admin);

        for (var i = 0; i < 5; i++)
            await svc.AuthenticateAsync(school.Id, "admin", "incorrecta");
        (await svc.AuthenticateAsync(school.Id, "admin", "correcta")).IsLockedOut.Should().BeTrue();

        clock.UtcNow = clock.UtcNow.AddMinutes(16); // pasó la ventana de 15 min
        var afterWait = await svc.AuthenticateAsync(school.Id, "admin", "correcta");
        afterWait.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Successful_login_clears_the_failed_attempt_counter()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var svc = NewService(db);
        await svc.CreateOperatorAsync(school.Id, "admin", "correcta", UserRole.Admin);

        for (var i = 0; i < 4; i++)
            await svc.AuthenticateAsync(school.Id, "admin", "incorrecta");
        (await svc.AuthenticateAsync(school.Id, "admin", "correcta")).Succeeded.Should().BeTrue();

        // Contador limpio: cuatro fallos nuevos siguen sin bloquear.
        for (var i = 0; i < 4; i++)
            (await svc.AuthenticateAsync(school.Id, "admin", "incorrecta")).IsLockedOut.Should().BeFalse();
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
