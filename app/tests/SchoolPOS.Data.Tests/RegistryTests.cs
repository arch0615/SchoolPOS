using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SchoolPOS.Data.Security;
using SchoolPOS.Data.Services;
using SchoolPOS.Data.Tests.TestSupport;
using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Data.Tests;

/// <summary>
/// Alta de alumnos y operadores (FR-ADM-1/2). Antes de esto ninguna pantalla del producto los
/// creaba — solo el sembrador de demostración —, así que una escuela recién instalada no podía
/// inscribir a nadie ni dar de alta a un cajero.
/// </summary>
public class RegistryTests
{
    private static StudentRegistry NewStudents(TestDatabase db) => new(db.Context, new TestClock());

    private static OperatorRegistry NewOperators(TestDatabase db) =>
        new(db.Context, new Pbkdf2PasswordHasher(), new TestClock());

    [Fact]
    public async Task Creating_a_student_also_creates_the_balance_account()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var svc = NewStudents(db);

        var student = await svc.CreateAsync(school.Id, "A-100", "Ana López", cardCode: "CARD-100");

        var ctx = db.NewContext();
        var account = await ctx.Accounts.SingleAsync(a => a.StudentId == student.Id);
        account.Balance.Should().Be(0m, "un alumno nuevo empieza sin saldo");
        (await ctx.Students.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Duplicate_enrollment_is_rejected_with_a_readable_message()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var svc = NewStudents(db);
        await svc.CreateAsync(school.Id, "A-100", "Ana López", null);

        var act = () => svc.CreateAsync(school.Id, "A-100", "Otro Alumno", null);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*A-100*");
    }

    /// <summary>
    /// La credencial es opcional y la mayoría de los alumnos no la trae. El índice único la
    /// admitía nula una sola vez en SQL Server (los NULL cuentan como valor), de modo que el
    /// segundo alumno sin credencial reventaba — pero en SQLite no, así que no se notaba.
    /// </summary>
    [Fact]
    public async Task Several_students_may_have_no_card_code()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var svc = NewStudents(db);

        await svc.CreateAsync(school.Id, "A-100", "Ana López", cardCode: null);
        await svc.CreateAsync(school.Id, "A-101", "Luis Pérez", cardCode: null);
        await svc.CreateAsync(school.Id, "A-102", "Sara Ruiz", cardCode: "   ");

        (await db.NewContext().Students.CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task Duplicate_card_code_is_rejected()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var svc = NewStudents(db);
        await svc.CreateAsync(school.Id, "A-100", "Ana López", "CARD-1");

        var act = () => svc.CreateAsync(school.Id, "A-101", "Luis Pérez", "CARD-1");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Search_finds_by_name_enrollment_or_card_ignoring_case()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var svc = NewStudents(db);
        await svc.CreateAsync(school.Id, "A-100", "Ana López", "CARD-1");
        await svc.CreateAsync(school.Id, "B-200", "Luis Pérez", null);

        (await svc.ListAsync(school.Id, "ana")).Should().ContainSingle();
        (await svc.ListAsync(school.Id, "b-200")).Should().ContainSingle();
        (await svc.ListAsync(school.Id, "card-1")).Should().ContainSingle();
        (await svc.ListAsync(school.Id)).Should().HaveCount(2);
    }

    [Fact]
    public async Task Deactivated_student_is_hidden_but_not_deleted()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var svc = NewStudents(db);
        var student = await svc.CreateAsync(school.Id, "A-100", "Ana López", null);

        await svc.SetActiveAsync(student.Id, false);

        (await svc.ListAsync(school.Id)).Should().BeEmpty();
        (await svc.ListAsync(school.Id, includeInactive: true)).Should().ContainSingle();
        (await db.NewContext().Students.CountAsync()).Should().Be(1, "la baja es lógica: su historial sigue");
    }

    [Fact]
    public async Task Operator_role_and_password_can_be_changed()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var hasher = new Pbkdf2PasswordHasher();
        var auth = new AuthService(db.Context, hasher, new TestClock());
        await auth.CreateOperatorAsync(school.Id, "admin", "clave123", UserRole.Admin);
        var cashier = await auth.CreateOperatorAsync(school.Id, "cajero", "clave123", UserRole.Cashier);
        var svc = NewOperators(db);

        await svc.SetRoleAsync(cashier.Id, UserRole.Warehouse);
        await svc.ResetPasswordAsync(cashier.Id, "nueva456");

        var rows = await svc.ListAsync(school.Id);
        rows.Single(r => r.Username == "cajero").Role.Should().Be(UserRole.Warehouse);
        (await auth.AuthenticateAsync(school.Id, "cajero", "nueva456")).Succeeded.Should().BeTrue();
        (await auth.AuthenticateAsync(school.Id, "cajero", "clave123")).Succeeded.Should().BeFalse();
    }

    /// <summary>
    /// Dejar la escuela sin administrador la deja sin acceso a configuración, reportes y a esta
    /// misma pantalla: nadie podría volver a nombrar uno.
    /// </summary>
    [Fact]
    public async Task The_last_administrator_cannot_be_demoted_or_deactivated()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var auth = new AuthService(db.Context, new Pbkdf2PasswordHasher(), new TestClock());
        var admin = await auth.CreateOperatorAsync(school.Id, "admin", "clave123", UserRole.Admin);
        await auth.CreateOperatorAsync(school.Id, "cajero", "clave123", UserRole.Cashier);
        var svc = NewOperators(db);

        var demote = () => svc.SetRoleAsync(admin.Id, UserRole.Cashier);
        await demote.Should().ThrowAsync<InvalidOperationException>();

        var deactivate = () => svc.SetActiveAsync(admin.Id, false);
        await deactivate.Should().ThrowAsync<InvalidOperationException>();

        // Con un segundo administrador sí se permite.
        await auth.CreateOperatorAsync(school.Id, "admin2", "clave123", UserRole.Admin);
        await svc.SetRoleAsync(admin.Id, UserRole.Cashier);
        (await svc.ListAsync(school.Id)).Single(r => r.Username == "admin").Role
            .Should().Be(UserRole.Cashier);
    }

    [Fact]
    public async Task Unlock_clears_a_lockout_without_waiting()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var clock = new TestClock();
        var auth = new AuthService(db.Context, new Pbkdf2PasswordHasher(), clock);
        var user = await auth.CreateOperatorAsync(school.Id, "cajero", "clave123", UserRole.Cashier);
        for (var i = 0; i < 5; i++)
            await auth.AuthenticateAsync(school.Id, "cajero", "mala");
        (await auth.AuthenticateAsync(school.Id, "cajero", "clave123")).IsLockedOut.Should().BeTrue();

        await NewOperators(db).UnlockAsync(user.Id);

        (await auth.AuthenticateAsync(school.Id, "cajero", "clave123")).Succeeded.Should().BeTrue();
    }
}
