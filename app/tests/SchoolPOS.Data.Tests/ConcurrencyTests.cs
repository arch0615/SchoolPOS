using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SchoolPOS.Data;
using SchoolPOS.Data.Services;
using SchoolPOS.Data.Tests.TestSupport;
using SchoolPOS.Domain.Enums;
using SchoolPOS.Domain.Exceptions;
using Xunit;

namespace SchoolPOS.Data.Tests;

/// <summary>
/// Pruebas de concurrencia / no-doble-gasto (NFR-1, hito 5.9). El servicio de saldo usa un UPDATE
/// condicional atómico a nivel de base de datos, por lo que cargos concurrentes nunca sobregiran ni
/// pierden actualizaciones. Aquí se prueba de forma ejecutable sobre SQLite (multi-conexión) y, si
/// se define la variable de entorno, de forma adversarial sobre SQL Server real.
/// </summary>
public class ConcurrencyTests
{
    private static readonly Guid Operator = Guid.NewGuid();

    /// <summary>Lanza todas las tareas a la vez con una barrera, para maximizar la contención.</summary>
    private static async Task<TResult[]> RaceAsync<TResult>(int count, Func<int, Task<TResult>> worker)
    {
        var gate = new TaskCompletionSource();
        var tasks = Enumerable.Range(0, count).Select(i => Task.Run(async () =>
        {
            await gate.Task;
            return await worker(i);
        })).ToArray();
        gate.SetResult();
        return await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task Concurrent_debits_never_overdraw_only_affordable_succeed()
    {
        using var db = new SqliteConcurrencyDb();
        var accountId = await db.SeedAccountAsync(balance: 100m); // alcanza para 10 cargos de 10
        var clock = new TestClock();

        var results = await RaceAsync(20, async i =>
        {
            try
            {
                await db.WithContextAsync(ctx =>
                    new BalanceService(ctx, clock).ChargeSaleAsync(accountId, 10m, $"V{i}", Operator));
                return true;
            }
            catch (InsufficientBalanceException)
            {
                return false;
            }
        });

        results.Count(ok => ok).Should().Be(10, "solo 10 cargos de 10 caben en 100");
        results.Count(ok => !ok).Should().Be(10, "los otros 10 se rechazan sin sobregirar");

        await db.WithContextAsync(async ctx =>
        {
            var balance = await ctx.Accounts.Where(a => a.Id == accountId).Select(a => a.Balance).SingleAsync();
            var movements = await ctx.BalanceMovements.Where(m => m.AccountId == accountId).Select(m => m.Amount).ToListAsync();

            balance.Should().Be(0m, "nunca por debajo de cero");
            movements.Count.Should().Be(10, "exactamente 10 asientos, uno por cargo aplicado");
            movements.Sum().Should().Be(-100m);
            return 0;
        });
    }

    [Fact]
    public async Task Concurrent_debits_and_credits_have_no_lost_updates()
    {
        using var db = new SqliteConcurrencyDb();
        var accountId = await db.SeedAccountAsync(balance: 200m);
        var clock = new TestClock();

        // 10 cargos de 10 (-100) y 10 abonos de 5 (+50), todos simultáneos.
        var results = await RaceAsync(20, i => db.WithContextAsync(ctx =>
        {
            var svc = new BalanceService(ctx, clock);
            return i < 10
                ? svc.ChargeSaleAsync(accountId, 10m, $"V{i}", Operator)
                : svc.RefundAsync(accountId, 5m, $"R{i}", Operator);
        }));

        results.Should().HaveCount(20);

        await db.WithContextAsync(async ctx =>
        {
            var balance = await ctx.Accounts.Where(a => a.Id == accountId).Select(a => a.Balance).SingleAsync();
            var movements = await ctx.BalanceMovements.Where(m => m.AccountId == accountId).Select(m => m.Amount).ToListAsync();

            // Sin actualizaciones perdidas: los 20 asientos se aplican y reconcilian.
            movements.Count.Should().Be(20);
            movements.Sum().Should().Be(-50m);          // -100 + 50
            balance.Should().Be(150m);                   // 200 + (-50)
            balance.Should().Be(200m + movements.Sum(), "el saldo reconcilia con el libro mayor");
            return 0;
        });
    }

    /// <summary>
    /// Versión adversarial sobre SQL Server real (aislamiento e índices de fila reales). Se ejecuta
    /// solo si <c>SCHOOLPOS_SQLSERVER_TESTS</c> apunta a un servidor donde se pueda crear una DB.
    /// </summary>
    [SkippableFact]
    public async Task SqlServer_no_double_spend_under_high_concurrency()
    {
        var baseConn = Environment.GetEnvironmentVariable("SCHOOLPOS_SQLSERVER_TESTS");
        Skip.If(string.IsNullOrWhiteSpace(baseConn),
            "Defina SCHOOLPOS_SQLSERVER_TESTS (cadena de conexión SQL Server) para ejecutar esta prueba de concurrencia real.");

        var dbName = $"SchoolPOS_Conc_{Guid.NewGuid():N}";
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(baseConn) { InitialCatalog = dbName };
        var connectionString = builder.ConnectionString;

        SchoolDbContext NewCtx() =>
            new(new DbContextOptionsBuilder<SchoolDbContext>().UseSqlServer(connectionString).Options);

        var clock = new TestClock();
        Guid accountId;
        try
        {
            await using (var ctx = NewCtx())
            {
                await ctx.Database.EnsureCreatedAsync();
                var schoolId = Guid.NewGuid();
                var student = new Domain.Entities.Student { SchoolId = schoolId, EnrollmentNo = "A-001", FullName = "Alumno" };
                var account = new Domain.Entities.Account { StudentId = student.Id, Balance = 500m };
                student.Account = account;
                ctx.Schools.Add(new Domain.Entities.School { Id = schoolId, Name = "Conc", Currency = "MXN" });
                ctx.Students.Add(student);
                ctx.Accounts.Add(account);
                await ctx.SaveChangesAsync();
                accountId = account.Id;
            }

            // 100 cargos concurrentes de 10 contra saldo 500 → exactamente 50 aplican.
            var results = await RaceAsync(100, async i =>
            {
                await using var ctx = NewCtx();
                try
                {
                    await new BalanceService(ctx, clock).ChargeSaleAsync(accountId, 10m, $"V{i}", Operator);
                    return true;
                }
                catch (InsufficientBalanceException) { return false; }
            });

            results.Count(ok => ok).Should().Be(50);
            await using var check = NewCtx();
            (await check.Accounts.Where(a => a.Id == accountId).Select(a => a.Balance).SingleAsync()).Should().Be(0m);
            (await check.BalanceMovements.CountAsync(m => m.AccountId == accountId && m.Type == MovementType.Sale))
                .Should().Be(50);
        }
        finally
        {
            await using var drop = NewCtx();
            await drop.Database.EnsureDeletedAsync();
        }
    }
}
