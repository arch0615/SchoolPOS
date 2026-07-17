using FluentAssertions;
using SchoolPOS.Data.Services;
using SchoolPOS.Data.Tests.TestSupport;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Entities;
using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Data.Tests;

public class CommissionInvoiceServiceTests
{
    private static readonly DateTime PeriodFrom = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PeriodTo = new(2026, 6, 30, 23, 59, 59, DateTimeKind.Utc);

    private static CommissionInvoiceService NewService(TestDatabase db, ICfdiIssuer? issuer = null) =>
        new(db.Context, new CommissionReportService(db.Context), issuer ?? new NullCfdiIssuer(),
            new CfdiSettings(), new TestClock());

    private static School SeedFiscalSchool(TestDatabase db, bool withFiscalData = true)
    {
        var school = new School
        {
            Name = "Colegio Demo",
            Currency = "MXN",
            CommissionRate = 0.05m,
            LegalName = "Colegio Demo SA de CV",
            Rfc = withFiscalData ? "XAXX010101000" : null,
            TaxRegime = withFiscalData ? "601" : null,
            PostalCode = withFiscalData ? "23089" : null,
            CfdiUse = withFiscalData ? "G03" : null,
        };
        db.Context.Schools.Add(school);
        db.Context.SaveChanges();
        db.Context.ChangeTracker.Clear();
        return school;
    }

    private static void SeedCapturedTopUp(TestDatabase db, Guid schoolId, Guid accountId, decimal amount, string gw)
    {
        db.Context.TopUps.Add(new TopUp
        {
            SchoolId = schoolId,
            AccountId = accountId,
            Amount = amount,
            CommissionRate = 0.05m,
            CommissionAmount = Math.Round(amount * 0.05m, 2, MidpointRounding.AwayFromZero),
            GatewayRef = gw,
            Status = TopUpStatus.Applied,
            AppliedLocally = true,
            CreatedAtUtc = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc),
        });
        db.Context.SaveChanges();
        db.Context.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Issues_and_persists_stamped_invoice_with_uuid()
    {
        using var db = new TestDatabase();
        var school = SeedFiscalSchool(db);
        var account = db.SeedStudentAccount(school.Id);
        SeedCapturedTopUp(db, school.Id, account.Id, 100m, "MP-1");
        SeedCapturedTopUp(db, school.Id, account.Id, 300m, "MP-2");
        var svc = NewService(db);

        var invoice = await svc.IssueForPeriodAsync(school.Id, PeriodFrom, PeriodTo);

        invoice.Status.Should().Be(CfdiStatus.Stamped);
        invoice.Uuid.Should().NotBeNullOrEmpty();
        invoice.CommissionAmount.Should().Be(20m); // 5% de 400
        invoice.StampedAtUtc.Should().NotBeNull();

        var persisted = await db.NewContext().CommissionInvoices.FindAsync(invoice.Id);
        persisted!.Uuid.Should().Be(invoice.Uuid);
    }

    [Fact]
    public async Task Throws_when_school_is_missing_fiscal_data()
    {
        using var db = new TestDatabase();
        var school = SeedFiscalSchool(db, withFiscalData: false);
        var account = db.SeedStudentAccount(school.Id);
        SeedCapturedTopUp(db, school.Id, account.Id, 100m, "MP-1");
        var svc = NewService(db);

        var act = () => svc.IssueForPeriodAsync(school.Id, PeriodFrom, PeriodTo);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*RFC*"); // el mensaje enumera los datos faltantes
    }

    [Fact]
    public async Task Throws_when_no_commission_in_period()
    {
        using var db = new TestDatabase();
        var school = SeedFiscalSchool(db);
        db.SeedStudentAccount(school.Id); // sin recargas

        var svc = NewService(db);
        var act = () => svc.IssueForPeriodAsync(school.Id, PeriodFrom, PeriodTo);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Records_failure_when_issuer_fails()
    {
        using var db = new TestDatabase();
        var school = SeedFiscalSchool(db);
        var account = db.SeedStudentAccount(school.Id);
        SeedCapturedTopUp(db, school.Id, account.Id, 100m, "MP-1");
        var svc = NewService(db, new FailingCfdiIssuer());

        var invoice = await svc.IssueForPeriodAsync(school.Id, PeriodFrom, PeriodTo);

        invoice.Status.Should().Be(CfdiStatus.Failed);
        invoice.Uuid.Should().BeNull();
        invoice.Error.Should().NotBeNullOrEmpty();
    }

    private sealed class FailingCfdiIssuer : ICfdiIssuer
    {
        public Task<CfdiResult> IssueAsync(CommissionInvoiceRequest request, CancellationToken ct = default)
            => Task.FromResult(CfdiResult.Fail("PAC no disponible"));
    }
}
