using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SchoolPOS.Data.Security;
using SchoolPOS.Data.Services;
using SchoolPOS.Data.Tests.TestSupport;
using SchoolPOS.Domain.Entities;
using SchoolPOS.Domain.Enums;

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
    public async Task Set_and_clear_daily_spend_limit_for_a_linked_student()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        db.SeedStudentAccount(school.Id, balance: 75m, enrollmentNo: "MAT-9");
        var svc = NewService(db);
        var guardian = await svc.RegisterAsync(school.Id, "p@c.com", "clave123", "P");
        await svc.LinkStudentByEnrollmentAsync(guardian.Id, school.Id, "MAT-9");
        var accountId = (await svc.GetLinkedStudentsAsync(guardian.Id))[0].AccountId;

        await svc.SetDailySpendLimitAsync(guardian.Id, accountId, 50m);
        (await svc.GetLinkedStudentsAsync(guardian.Id))[0].DailySpendLimit.Should().Be(50m);

        await svc.SetDailySpendLimitAsync(guardian.Id, accountId, null); // quitar el límite
        (await svc.GetLinkedStudentsAsync(guardian.Id))[0].DailySpendLimit.Should().BeNull();
    }

    /// <summary>Un tutor no puede limitar el gasto de un alumno que no le pertenece.</summary>
    [Fact]
    public async Task Cannot_set_daily_limit_for_a_student_not_owned_by_the_guardian()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var otherAccount = db.SeedStudentAccount(school.Id, balance: 0m, enrollmentNo: "MAT-OTHER");
        var svc = NewService(db);
        var guardian = await svc.RegisterAsync(school.Id, "p@c.com", "clave123", "P");
        // guardian nunca vincula a "MAT-OTHER" — adivina el AccountId directamente.

        var act = () => svc.SetDailySpendLimitAsync(guardian.Id, otherAccount.Id, 50m);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Get_sale_items_returns_line_detail_only_for_the_guardians_own_account()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var myAccount = db.SeedStudentAccount(school.Id, enrollmentNo: "MAT-MINE");
        var otherAccount = db.SeedStudentAccount(school.Id, enrollmentNo: "MAT-OTHER");
        var svc = NewService(db);
        var guardian = await svc.RegisterAsync(school.Id, "p@c.com", "clave123", "P");
        await svc.LinkStudentByEnrollmentAsync(guardian.Id, school.Id, "MAT-MINE");

        var mySale = new Sale
        {
            SchoolId = school.Id, AccountId = myAccount.Id, Tender = TenderType.Balance,
            Status = SaleStatus.Completed, Total = 45m, CreatedAtUtc = DateTime.UtcNow,
            Lines = new List<SaleLine> { new() { Description = "Torta", Quantity = 1, UnitPrice = 45m, LineTotal = 45m } },
        };
        var otherSale = new Sale
        {
            SchoolId = school.Id, AccountId = otherAccount.Id, Tender = TenderType.Balance,
            Status = SaleStatus.Completed, Total = 20m, CreatedAtUtc = DateTime.UtcNow,
            Lines = new List<SaleLine> { new() { Description = "Jugo", Quantity = 1, UnitPrice = 20m, LineTotal = 20m } },
        };
        db.Context.Sales.AddRange(mySale, otherSale);
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        // Pide ambas ventas, pero acotado a myAccount: no debe revelar nada de otherSale.
        var result = await svc.GetSaleItemsAsync(myAccount.Id, new[] { mySale.Id, otherSale.Id });

        result.Should().ContainKey(mySale.Id);
        result[mySale.Id].Should().ContainSingle(i => i.Description == "Torta");
        result.Should().NotContainKey(otherSale.Id, "esa venta es de una cuenta que no le pertenece a este tutor");
    }

    [Fact]
    public async Task Register_without_accepting_terms_or_privacy_is_rejected()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var svc = NewService(db);

        var noTerms = () => svc.RegisterAsync(school.Id, "p@c.com", "clave123", "P", acceptedTerms: false);
        await noTerms.Should().ThrowAsync<ArgumentException>();

        var noPrivacy = () => svc.RegisterAsync(school.Id, "p@c.com", "clave123", "P", acceptedPrivacy: false);
        await noPrivacy.Should().ThrowAsync<ArgumentException>();

        // Ninguno de los dos intentos debe haber dejado una cuenta a medias.
        (await db.Context.Guardians.AnyAsync(g => g.SchoolId == school.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task Register_records_consent_timestamps_and_initial_notification_preferences()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var svc = NewService(db);

        var accepted = await svc.RegisterAsync(
            school.Id, "acepta@c.com", "clave123", "Acepta", acceptedTerms: true, acceptedPrivacy: true,
            acceptNotifications: true);
        accepted.AcceptedTermsAtUtc.Should().NotBeNull();
        accepted.AcceptedPrivacyAtUtc.Should().NotBeNull();

        var withPrefs = await db.Context.GuardianNotificationPreferences
            .SingleAsync(p => p.GuardianId == accepted.Id);
        withPrefs.LowBalance.Should().BeTrue();
        withPrefs.TopUpConfirmed.Should().BeTrue();

        // Casilla de notificaciones sin marcar: nada debe quedar prendido por omisión.
        var declined = await svc.RegisterAsync(
            school.Id, "declina@c.com", "clave123", "Declina", acceptedTerms: true, acceptedPrivacy: true,
            acceptNotifications: false);
        var withoutPrefs = await db.Context.GuardianNotificationPreferences
            .SingleAsync(p => p.GuardianId == declined.Id);
        withoutPrefs.LowBalance.Should().BeFalse();
        withoutPrefs.TopUpConfirmed.Should().BeFalse();
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
