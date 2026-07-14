using FluentAssertions;
using SchoolPOS.Data.Security;
using SchoolPOS.Data.Services;
using SchoolPOS.Data.Tests.TestSupport;

namespace SchoolPOS.Data.Tests;

public class GuardianServiceTests
{
    private static GuardianService NewService(TestDatabase db, TestClock? clock = null) =>
        new(db.Context, new Pbkdf2PasswordHasher(), clock ?? new TestClock());

    [Fact]
    public async Task Register_then_authenticate_succeeds()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var svc = NewService(db);
        await svc.RegisterAsync(school.Id, "Padre@Correo.com", "clave123", "Padre Uno");

        var result = await svc.AuthenticateAsync(school.Id, "padre@correo.com", "clave123");

        result.Succeeded.Should().BeTrue();
        result.Guardian!.FullName.Should().Be("Padre Uno");
    }

    [Fact]
    public async Task Duplicate_email_is_rejected()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var svc = NewService(db);
        await svc.RegisterAsync(school.Id, "p@c.com", "clave123", "P");

        var act = () => svc.RegisterAsync(school.Id, "p@c.com", "otra123", "Q");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Account_locks_after_five_failed_attempts()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var svc = NewService(db);
        await svc.RegisterAsync(school.Id, "p@c.com", "correcta", "P");

        for (var i = 0; i < 4; i++)
        {
            var r = await svc.AuthenticateAsync(school.Id, "p@c.com", "mala");
            r.Succeeded.Should().BeFalse();
            r.IsLockedOut.Should().BeFalse();
        }

        // Quinto intento fallido -> bloqueo.
        var fifth = await svc.AuthenticateAsync(school.Id, "p@c.com", "mala");
        fifth.IsLockedOut.Should().BeTrue();

        // Aun con la contraseña correcta, sigue bloqueada.
        var locked = await svc.AuthenticateAsync(school.Id, "p@c.com", "correcta");
        locked.Succeeded.Should().BeFalse();
        locked.IsLockedOut.Should().BeTrue();
    }

    [Fact]
    public async Task Lockout_expires_and_login_succeeds()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var clock = new TestClock();
        var svc = NewService(db, clock);
        await svc.RegisterAsync(school.Id, "p@c.com", "correcta", "P");
        for (var i = 0; i < 5; i++)
            await svc.AuthenticateAsync(school.Id, "p@c.com", "mala");

        clock.UtcNow = clock.UtcNow.AddMinutes(16); // pasa el bloqueo

        var result = await svc.AuthenticateAsync(school.Id, "p@c.com", "correcta");
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Link_student_by_enrollment_and_list_with_balance()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var account = db.SeedStudentAccount(school.Id, balance: 75m, enrollmentNo: "MAT-9");
        var svc = NewService(db);
        var guardian = await svc.RegisterAsync(school.Id, "p@c.com", "clave123", "P");

        await svc.LinkStudentByEnrollmentAsync(guardian.Id, school.Id, "MAT-9");
        var linked = await svc.GetLinkedStudentsAsync(guardian.Id);

        linked.Should().ContainSingle();
        linked[0].Balance.Should().Be(75m);
        linked[0].AccountId.Should().Be(account.Id);
        (await svc.OwnsStudentAsync(guardian.Id, linked[0].StudentId)).Should().BeTrue();
    }

    [Fact]
    public async Task Link_unknown_enrollment_throws()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var svc = NewService(db);
        var guardian = await svc.RegisterAsync(school.Id, "p@c.com", "clave123", "P");

        var act = () => svc.LinkStudentByEnrollmentAsync(guardian.Id, school.Id, "NOPE");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
