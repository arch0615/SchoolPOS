using FluentAssertions;
using SchoolPOS.Data.Services;
using SchoolPOS.Data.Tests.TestSupport;
using SchoolPOS.Domain.Entities;

namespace SchoolPOS.Data.Tests;

public class PosLookupTests
{
    [Fact]
    public async Task FindByBarcode_returns_active_product()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var product = new Product { SchoolId = school.Id, Name = "Agua", Barcode = "7501000111", Price = 12m, IsActive = true };
        db.Context.Products.Add(product);
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var svc = new InventoryService(db.Context, new TestClock());
        var found = await svc.FindByBarcodeAsync(school.Id, "7501000111");

        found.Should().NotBeNull();
        found!.Name.Should().Be("Agua");
    }

    [Fact]
    public async Task Student_directory_finds_by_enrollment_and_card_with_balance()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var student = new Student { SchoolId = school.Id, EnrollmentNo = "MAT-77", CardCode = "CARD-77", FullName = "Ana" };
        var account = new Account { StudentId = student.Id, Balance = 42.50m };
        student.Account = account;
        db.Context.Students.Add(student);
        db.Context.Accounts.Add(account);
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var dir = new StudentDirectory(db.Context);

        var byEnrollment = await dir.FindByCodeAsync(school.Id, "MAT-77");
        var byCard = await dir.FindByCodeAsync(school.Id, "CARD-77");
        var missing = await dir.FindByCodeAsync(school.Id, "NOPE");

        byEnrollment.Should().NotBeNull();
        byEnrollment!.Balance.Should().Be(42.50m);
        byEnrollment.FullName.Should().Be("Ana");
        byCard.Should().NotBeNull();
        byCard!.AccountId.Should().Be(account.Id);
        missing.Should().BeNull();
    }
}
