using Microsoft.EntityFrameworkCore;
using SchoolPOS.Domain.Entities;

namespace SchoolPOS.Data;

/// <summary>
/// Contexto de EF Core para la DB local de una escuela (fuente única de verdad).
/// El importe monetario se persiste como <c>decimal(18,4)</c> (NFR-4). El libro mayor
/// (<see cref="BalanceMovement"/>) es de solo inserción (inmutable, NFR-5).
/// </summary>
public class SchoolDbContext : DbContext
{
    public SchoolDbContext(DbContextOptions<SchoolDbContext> options) : base(options) { }

    public DbSet<School> Schools => Set<School>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Guardian> Guardians => Set<Guardian>();
    public DbSet<GuardianStudent> GuardianStudents => Set<GuardianStudent>();
    public DbSet<BalanceMovement> BalanceMovements => Set<BalanceMovement>();
    public DbSet<TopUp> TopUps => Set<TopUp>();
    public DbSet<User> Users => Set<User>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    // Inventario
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    // Ventas
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleLine> SaleLines => Set<SaleLine>();

    // Compras
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<GoodsReceipt> GoodsReceipts => Set<GoodsReceipt>();
    public DbSet<GoodsReceiptLine> GoodsReceiptLines => Set<GoodsReceiptLine>();
    public DbSet<SupplierInvoice> SupplierInvoices => Set<SupplierInvoice>();

    // Tesorería
    public DbSet<CashSession> CashSessions => Set<CashSession>();
    public DbSet<CashMovement> CashMovements => Set<CashMovement>();

    // Facturación de comisión (CFDI)
    public DbSet<CommissionInvoice> CommissionInvoices => Set<CommissionInvoice>();

    // Cuentas de pago conectadas por OAuth (marketplace)
    public DbSet<SchoolPaymentAccount> SchoolPaymentAccounts => Set<SchoolPaymentAccount>();

    /// <summary>Precisión estándar del dinero en toda la DB.</summary>
    private const int MoneyPrecision = 18;
    private const int MoneyScale = 4;

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // Toda propiedad decimal usa precisión monetaria por defecto (evita deriva por redondeo).
        foreach (var property in b.Model.GetEntityTypes()
                     .SelectMany(t => t.GetProperties())
                     .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
        {
            property.SetPrecision(MoneyPrecision);
            property.SetScale(MoneyScale);
        }

        b.Entity<School>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            e.Property(x => x.Rfc).HasMaxLength(13);
            e.Property(x => x.LegalName).HasMaxLength(250);
            e.Property(x => x.TaxRegime).HasMaxLength(10);
            e.Property(x => x.PostalCode).HasMaxLength(10);
            e.Property(x => x.CfdiUse).HasMaxLength(10);
        });

        b.Entity<CommissionInvoice>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Uuid).HasMaxLength(36);
            e.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            e.HasIndex(x => new { x.SchoolId, x.PeriodFromUtc, x.PeriodToUtc });
        });

        b.Entity<SchoolPaymentAccount>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Provider).HasMaxLength(50).IsRequired();
            e.Property(x => x.ProviderUserId).HasMaxLength(100);
            e.Property(x => x.AccessToken).HasMaxLength(1000).IsRequired();
            e.Property(x => x.RefreshToken).HasMaxLength(1000);
            e.HasIndex(x => new { x.SchoolId, x.Provider }).IsUnique();
        });

        b.Entity<Student>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.EnrollmentNo).HasMaxLength(50).IsRequired();
            e.Property(x => x.CardCode).HasMaxLength(100);
            e.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            // Matrícula única por escuela.
            e.HasIndex(x => new { x.SchoolId, x.EnrollmentNo }).IsUnique();
            // Código de credencial único por escuela (cuando existe).
            e.HasIndex(x => new { x.SchoolId, x.CardCode })
                .IsUnique()
                .HasFilter(null);
            e.HasOne(x => x.School).WithMany().HasForeignKey(x => x.SchoolId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Account>(e =>
        {
            e.HasKey(x => x.Id);
            // 1:1 Student <-> Account.
            e.HasIndex(x => x.StudentId).IsUnique();
            e.HasOne(x => x.Student).WithOne(s => s.Account)
                .HasForeignKey<Account>(x => x.StudentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<BalanceMovement>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Reference).HasMaxLength(100);
            e.HasIndex(x => new { x.AccountId, x.CreatedAtUtc });
            e.HasOne(x => x.Account).WithMany(a => a.Movements)
                .HasForeignKey(x => x.AccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<TopUp>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.GatewayRef).HasMaxLength(100).IsRequired();
            // Deduplicación idempotente por referencia de la pasarela.
            e.HasIndex(x => x.GatewayRef).IsUnique();
            e.HasOne(x => x.Account).WithMany()
                .HasForeignKey(x => x.AccountId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Guardian>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Email).HasMaxLength(256).IsRequired();
            e.Property(x => x.FullName).HasMaxLength(200);
            e.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired();
            e.Property(x => x.PasswordResetTokenHash).HasMaxLength(500);
            e.HasIndex(x => new { x.SchoolId, x.Email }).IsUnique();
        });

        b.Entity<GuardianStudent>(e =>
        {
            e.HasKey(x => new { x.GuardianId, x.StudentId });
            e.HasOne(x => x.Guardian).WithMany(g => g.Students)
                .HasForeignKey(x => x.GuardianId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Student).WithMany()
                .HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<User>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Username).HasMaxLength(100).IsRequired();
            e.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired();
            e.HasIndex(x => new { x.SchoolId, x.Username }).IsUnique();
        });

        b.Entity<AuditLog>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Actor).HasMaxLength(200);
            e.Property(x => x.Action).HasMaxLength(100);
            e.Property(x => x.Entity).HasMaxLength(100);
            e.Property(x => x.EntityId).HasMaxLength(100);
            e.HasIndex(x => new { x.SchoolId, x.CreatedAtUtc });
        });

        ConfigureInventory(b);
        ConfigureSales(b);
        ConfigurePurchasing(b);
        ConfigureTreasury(b);
    }

    private static void ConfigureInventory(ModelBuilder b)
    {
        b.Entity<Category>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.HasIndex(x => new { x.SchoolId, x.Name }).IsUnique();
        });

        b.Entity<Product>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Barcode).HasMaxLength(100);
            // Código de barras único por escuela (cuando existe).
            e.HasIndex(x => new { x.SchoolId, x.Barcode }).IsUnique().HasFilter(null);
            e.HasOne(x => x.Category).WithMany()
                .HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<StockMovement>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Reference).HasMaxLength(100);
            e.Property(x => x.Reason).HasMaxLength(300);
            e.HasIndex(x => new { x.ProductId, x.CreatedAtUtc });
            e.HasOne(x => x.Product).WithMany(p => p.StockMovements)
                .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureSales(ModelBuilder b)
    {
        b.Entity<Sale>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.SchoolId, x.CreatedAtUtc });
            e.HasIndex(x => x.AccountId);
        });

        b.Entity<SaleLine>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Description).HasMaxLength(200).IsRequired();
            e.HasOne(x => x.Sale).WithMany(s => s.Lines)
                .HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.ProductId);
        });
    }

    private static void ConfigurePurchasing(ModelBuilder b)
    {
        b.Entity<Supplier>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Rfc).HasMaxLength(20);
            e.Property(x => x.ContactName).HasMaxLength(150);
            e.Property(x => x.Phone).HasMaxLength(50);
            e.Property(x => x.Email).HasMaxLength(256);
            e.HasIndex(x => new { x.SchoolId, x.Name });
        });

        b.Entity<PurchaseOrder>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.OrderNumber).HasMaxLength(50).IsRequired();
            e.HasIndex(x => new { x.SchoolId, x.OrderNumber }).IsUnique();
            e.HasOne(x => x.Supplier).WithMany()
                .HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<PurchaseOrderLine>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.PurchaseOrder).WithMany(p => p.Lines)
                .HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.ProductId);
        });

        b.Entity<GoodsReceipt>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.HasOne(x => x.PurchaseOrder).WithMany(p => p.Receipts)
                .HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<GoodsReceiptLine>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.GoodsReceipt).WithMany(g => g.Lines)
                .HasForeignKey(x => x.GoodsReceiptId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.ProductId);
        });

        b.Entity<SupplierInvoice>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.InvoiceNumber).HasMaxLength(50).IsRequired();
            e.HasIndex(x => new { x.SchoolId, x.SupplierId, x.InvoiceNumber }).IsUnique();
            e.HasOne(x => x.Supplier).WithMany()
                .HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureTreasury(ModelBuilder b)
    {
        b.Entity<CashSession>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.SchoolId, x.Status });
        });

        b.Entity<CashMovement>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Reason).HasMaxLength(300).IsRequired();
            e.HasOne(x => x.CashSession).WithMany(s => s.Movements)
                .HasForeignKey(x => x.CashSessionId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
