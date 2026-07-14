using FluentAssertions;
using SchoolPOS.Data.Services;
using SchoolPOS.Data.Tests.TestSupport;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Entities;

namespace SchoolPOS.Data.Tests;

public class PurchasingReportServiceTests
{
    private static PurchasingService NewPurchasing(TestDatabase db)
    {
        var clock = new TestClock();
        return new PurchasingService(db.Context, new InventoryService(db.Context, clock), clock);
    }

    private static Supplier SeedSupplier(TestDatabase db, Guid schoolId, string name)
    {
        var s = new Supplier { SchoolId = schoolId, Name = name };
        db.Context.Suppliers.Add(s);
        db.Context.SaveChanges();
        db.Context.ChangeTracker.Clear();
        return s;
    }

    [Fact]
    public async Task Reports_aggregate_by_supplier_product_and_period()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var supA = SeedSupplier(db, school.Id, "Prov A");
        var supB = SeedSupplier(db, school.Id, "Prov B");
        var p1 = db.SeedProduct(school.Id, name: "P1");
        var p2 = db.SeedProduct(school.Id, name: "P2");
        var purchasing = NewPurchasing(db);

        // Prov A: 10xP1@6 = 60 ; Prov B: 5xP1@6 (30) + 2xP2@20 (40) = 70
        await purchasing.CreateOrderAsync(school.Id, supA.Id, "OC-1",
            new[] { new PurchaseOrderLineRequest(p1.Id, 10m, 6m) }, null, null);
        await purchasing.CreateOrderAsync(school.Id, supB.Id, "OC-2",
            new[] { new PurchaseOrderLineRequest(p1.Id, 5m, 6m), new PurchaseOrderLineRequest(p2.Id, 2m, 20m) }, null, null);

        var reports = new PurchasingReportService(db.Context);

        var summary = await reports.GetSummaryAsync(school.Id, null, null);
        summary.OrderCount.Should().Be(2);
        summary.Total.Should().Be(130m);

        var bySupplier = await reports.GetBySupplierAsync(school.Id, null, null);
        bySupplier.Single(r => r.SupplierName == "Prov A").Total.Should().Be(60m);
        bySupplier.Single(r => r.SupplierName == "Prov B").Total.Should().Be(70m);

        var byProduct = await reports.GetByProductAsync(school.Id, null, null);
        byProduct.Single(r => r.ProductId == p1.Id).Quantity.Should().Be(15m);  // 10 + 5
        byProduct.Single(r => r.ProductId == p1.Id).Total.Should().Be(90m);      // 60 + 30
        byProduct.Single(r => r.ProductId == p2.Id).Total.Should().Be(40m);
    }

    [Fact]
    public async Task Cancelled_orders_are_excluded()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var sup = SeedSupplier(db, school.Id, "Prov");
        var p = db.SeedProduct(school.Id);
        var purchasing = NewPurchasing(db);

        var order = await purchasing.CreateOrderAsync(school.Id, sup.Id, "OC-9",
            new[] { new PurchaseOrderLineRequest(p.Id, 10m, 6m) }, null, null);

        // Cancelar directamente en la DB.
        var tracked = await db.Context.PurchaseOrders.FindAsync(order.Id);
        tracked!.Status = Domain.Enums.PurchaseOrderStatus.Cancelled;
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var reports = new PurchasingReportService(db.Context);
        (await reports.GetSummaryAsync(school.Id, null, null)).OrderCount.Should().Be(0);
        (await reports.GetByProductAsync(school.Id, null, null)).Should().BeEmpty();
    }
}
