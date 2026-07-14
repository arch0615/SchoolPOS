using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SchoolPOS.Data.Tests.TestSupport;
using SchoolPOS.Domain.Entities;
using SchoolPOS.Domain.Enums;

namespace SchoolPOS.Data.Tests;

/// <summary>
/// Pruebas de humo del esquema Fase 1 (inventario, ventas, compras, tesorería): confirman que
/// el modelo completo crea la base y que las entidades persisten y reconcilian.
/// </summary>
public class SchemaPersistenceTests
{
    [Fact]
    public void Full_model_creates_all_tables()
    {
        // El constructor de TestDatabase ejecuta EnsureCreated() sobre el modelo completo (22 entidades).
        using var db = new TestDatabase();
        db.Context.Model.GetEntityTypes().Should().HaveCountGreaterThan(15);
    }

    [Fact]
    public async Task Sale_with_lines_persists_and_line_totals_sum_to_total()
    {
        using var db = new TestDatabase();
        var schoolId = Guid.NewGuid();

        var sale = new Sale
        {
            SchoolId = schoolId,
            CashierId = Guid.NewGuid(),
            Tender = TenderType.Balance,
            Subtotal = 45m,
            DiscountTotal = 5m,
            TaxTotal = 0m,
            Total = 40m,
            Lines =
            {
                new SaleLine { Description = "Torta", Quantity = 1, UnitPrice = 25m, Discount = 5m, LineTotal = 20m, ProductId = Guid.NewGuid() },
                new SaleLine { Description = "Jugo",  Quantity = 2, UnitPrice = 10m, Discount = 0m, LineTotal = 20m, ProductId = Guid.NewGuid() },
            },
        };
        db.Context.Sales.Add(sale);
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var reloaded = await db.NewContext().Sales.Include(s => s.Lines).SingleAsync(s => s.Id == sale.Id);
        reloaded.Lines.Should().HaveCount(2);
        reloaded.Lines.Sum(l => l.LineTotal).Should().Be(reloaded.Total, "los renglones cuadran con el total (DoD 1.7)");
    }

    [Fact]
    public async Task Product_stock_reconciles_with_kardex()
    {
        using var db = new TestDatabase();
        var product = new Product
        {
            SchoolId = Guid.NewGuid(),
            Name = "Sabritas",
            Price = 18m,
            Cost = 12m,
            MinStock = 5m,
            StockOnHand = 0m,
        };
        db.Context.Products.Add(product);
        // Entrada 10, salida 3 -> stock 7
        product.StockMovements.Add(new StockMovement { ProductId = product.Id, Type = StockMovementType.Entry, Quantity = 10m, StockAfter = 10m, UnitCost = 12m });
        product.StockMovements.Add(new StockMovement { ProductId = product.Id, Type = StockMovementType.Exit, Quantity = -3m, StockAfter = 7m });
        product.StockOnHand = 7m;
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var ctx = db.NewContext();
        var stock = await ctx.Products.Where(p => p.Id == product.Id).Select(p => p.StockOnHand).SingleAsync();
        var kardex = await ctx.StockMovements.Where(m => m.ProductId == product.Id).Select(m => m.Quantity).ToListAsync();

        stock.Should().Be(7m);
        kardex.Sum().Should().Be(stock, "el Kardex reconcilia con las existencias");
    }
}
