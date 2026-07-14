using FluentAssertions;
using SchoolPOS.Data.Services;
using SchoolPOS.Data.Tests.TestSupport;
using SchoolPOS.Domain.Entities;
using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Data.Tests;

public class CommissionReportServiceTests
{
    private static void SeedTopUp(TestDatabase db, Guid schoolId, Guid accountId,
        decimal amount, decimal rate, TopUpStatus status, string reference)
    {
        db.Context.TopUps.Add(new TopUp
        {
            SchoolId = schoolId,
            AccountId = accountId,
            Amount = amount,
            CommissionRate = rate,
            CommissionAmount = Math.Round(amount * rate, 2, MidpointRounding.AwayFromZero),
            GatewayRef = reference,
            Status = status,
            AppliedLocally = status == TopUpStatus.Applied,
        });
        db.Context.SaveChanges();
        db.Context.ChangeTracker.Clear();
    }

    [Fact]
    public async Task School_summary_sums_only_captured_topups()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool(commissionRate: 0.05m);
        var account = db.SeedStudentAccount(school.Id);
        SeedTopUp(db, school.Id, account.Id, 100m, 0.05m, TopUpStatus.Applied, "A");
        SeedTopUp(db, school.Id, account.Id, 200m, 0.05m, TopUpStatus.Confirmed, "B");
        SeedTopUp(db, school.Id, account.Id, 999m, 0.05m, TopUpStatus.Pending, "C"); // no cuenta
        SeedTopUp(db, school.Id, account.Id, 50m, 0.05m, TopUpStatus.Failed, "D");    // no cuenta

        var svc = new CommissionReportService(db.Context);
        var summary = await svc.GetSchoolSummaryAsync(school.Id, null, null);

        summary.TopUpCount.Should().Be(2);
        summary.TotalRecharged.Should().Be(300m);
        summary.TotalCommission.Should().Be(15m); // 5% de 300
    }

    [Fact]
    public async Task Vendor_rollup_aggregates_across_schools_with_breakdown()
    {
        using var db = new TestDatabase();
        var schoolA = db.SeedSchool(commissionRate: 0.05m);
        var accA = db.SeedStudentAccount(schoolA.Id, enrollmentNo: "A-1");
        var schoolB = db.SeedSchool(commissionRate: 0.03m);
        var accB = db.SeedStudentAccount(schoolB.Id, enrollmentNo: "B-1");

        SeedTopUp(db, schoolA.Id, accA.Id, 100m, 0.05m, TopUpStatus.Applied, "A1");
        SeedTopUp(db, schoolA.Id, accA.Id, 100m, 0.05m, TopUpStatus.Applied, "A2");
        SeedTopUp(db, schoolB.Id, accB.Id, 200m, 0.03m, TopUpStatus.Confirmed, "B1");

        var svc = new CommissionReportService(db.Context);
        var rollup = await svc.GetVendorRollupAsync(null, null);

        rollup.TopUpCount.Should().Be(3);
        rollup.TotalRecharged.Should().Be(400m);       // 100+100+200
        rollup.TotalCommission.Should().Be(16m);        // 5+5 + 6
        rollup.Schools.Should().HaveCount(2);

        var a = rollup.Schools.Single(s => s.SchoolId == schoolA.Id);
        a.TotalRecharged.Should().Be(200m);
        a.TotalCommission.Should().Be(10m);
        var b = rollup.Schools.Single(s => s.SchoolId == schoolB.Id);
        b.TotalCommission.Should().Be(6m);
    }

    [Fact]
    public async Task Date_filter_limits_the_range()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var account = db.SeedStudentAccount(school.Id);
        // Ambas recargas usan CreatedAtUtc por defecto (default DateTime) = 0001-01-01.
        SeedTopUp(db, school.Id, account.Id, 100m, 0.05m, TopUpStatus.Applied, "X");

        var svc = new CommissionReportService(db.Context);
        var future = await svc.GetVendorRollupAsync(new DateTime(2030, 1, 1), null);

        future.TopUpCount.Should().Be(0, "no hay recargas después de la fecha 'desde'");
        future.TotalCommission.Should().Be(0m);
    }
}
