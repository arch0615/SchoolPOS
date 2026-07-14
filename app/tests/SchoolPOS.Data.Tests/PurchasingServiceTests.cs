using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SchoolPOS.Data.Services;
using SchoolPOS.Data.Tests.TestSupport;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Entities;
using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Data.Tests;

public class PurchasingServiceTests
{
    private static readonly Guid Receiver = Guid.NewGuid();

    private static PurchasingService NewService(TestDatabase db)
    {
        var clock = new TestClock();
        return new PurchasingService(db.Context, new InventoryService(db.Context, clock), clock);
    }

    private static Supplier SeedSupplier(TestDatabase db, Guid schoolId)
    {
        var supplier = new Supplier { SchoolId = schoolId, Name = "Distribuidora X" };
        db.Context.Suppliers.Add(supplier);
        db.Context.SaveChanges();
        db.Context.ChangeTracker.Clear();
        return supplier;
    }

    [Fact]
    public async Task CreateOrder_computes_total_and_starts_in_draft()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var supplier = SeedSupplier(db, school.Id);
        var product = db.SeedProduct(school.Id);
        var svc = NewService(db);

        var order = await svc.CreateOrderAsync(school.Id, supplier.Id, "OC-100",
            new[] { new PurchaseOrderLineRequest(product.Id, 10m, 6m) }, expectedDate: null, notes: null);

        order.Status.Should().Be(PurchaseOrderStatus.Draft);
        order.Total.Should().Be(60m);
    }

    [Fact]
    public async Task ReceiveGoods_full_increments_stock_and_marks_received()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var supplier = SeedSupplier(db, school.Id);
        var product = db.SeedProduct(school.Id, stock: 0m);
        var svc = NewService(db);

        var order = await svc.CreateOrderAsync(school.Id, supplier.Id, "OC-101",
            new[] { new PurchaseOrderLineRequest(product.Id, 10m, 6m) }, null, null);
        var poLineId = (await db.NewContext().PurchaseOrderLines.SingleAsync(l => l.PurchaseOrderId == order.Id)).Id;

        await svc.ReceiveGoodsAsync(order.Id,
            new[] { new ReceiptLineRequest(product.Id, 10m, 6m, poLineId) }, Receiver, "Completa");

        var ctx = db.NewContext();
        (await ctx.Products.Where(p => p.Id == product.Id).Select(p => p.StockOnHand).SingleAsync()).Should().Be(10m);
        var reloaded = await ctx.PurchaseOrders.Include(o => o.Lines).SingleAsync(o => o.Id == order.Id);
        reloaded.Status.Should().Be(PurchaseOrderStatus.Received);
        reloaded.Lines.Single().QuantityReceived.Should().Be(10m);
        (await ctx.StockMovements.CountAsync(m => m.ProductId == product.Id && m.Type == StockMovementType.Entry)).Should().Be(1);
    }

    [Fact]
    public async Task ReceiveGoods_partial_marks_partially_received()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var supplier = SeedSupplier(db, school.Id);
        var product = db.SeedProduct(school.Id, stock: 0m);
        var svc = NewService(db);

        var order = await svc.CreateOrderAsync(school.Id, supplier.Id, "OC-102",
            new[] { new PurchaseOrderLineRequest(product.Id, 10m, 6m) }, null, null);
        var poLineId = (await db.NewContext().PurchaseOrderLines.SingleAsync(l => l.PurchaseOrderId == order.Id)).Id;

        await svc.ReceiveGoodsAsync(order.Id,
            new[] { new ReceiptLineRequest(product.Id, 4m, 6m, poLineId) }, Receiver, "Parcial");

        var ctx = db.NewContext();
        (await ctx.Products.Where(p => p.Id == product.Id).Select(p => p.StockOnHand).SingleAsync()).Should().Be(4m);
        (await ctx.PurchaseOrders.Where(o => o.Id == order.Id).Select(o => o.Status).SingleAsync())
            .Should().Be(PurchaseOrderStatus.PartiallyReceived);
    }

    [Fact]
    public async Task Invoice_payment_transitions_status_to_partial_then_paid()
    {
        using var db = new TestDatabase();
        var school = db.SeedSchool();
        var supplier = SeedSupplier(db, school.Id);
        var svc = NewService(db);

        var invoice = await svc.RegisterInvoiceAsync(
            school.Id, supplier.Id, orderId: null, "F-1", amount: 100m, issueDate: new DateTime(2026, 1, 1), dueDate: null);
        invoice.Status.Should().Be(SupplierInvoiceStatus.Pending);

        var afterPartial = await svc.RegisterInvoicePaymentAsync(invoice.Id, 40m);
        afterPartial.Status.Should().Be(SupplierInvoiceStatus.PartiallyPaid);

        var afterFull = await svc.RegisterInvoicePaymentAsync(invoice.Id, 60m);
        afterFull.Status.Should().Be(SupplierInvoiceStatus.Paid);
        afterFull.AmountPaid.Should().Be(100m);
    }
}
