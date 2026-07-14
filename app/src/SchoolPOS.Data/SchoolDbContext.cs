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
    }
}
