using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SchoolPOS.Data.Services;
using SchoolPOS.Data.Tests.TestSupport;
using SchoolPOS.Domain.Entities;
using SchoolPOS.Domain.Enums;
using SchoolPOS.Domain.Exceptions;

namespace SchoolPOS.Data.Tests;

public class BalanceServiceTests
{
    private static readonly Guid Operator = Guid.NewGuid();

    private static BalanceService NewService(TestDatabase db) =>
        new(db.Context, new TestClock());

    private static TopUp SeedTopUp(TestDatabase db, Guid accountId, decimal amount, string gatewayRef)
    {
        var topUp = new TopUp
        {
            SchoolId = Guid.NewGuid(),
            AccountId = accountId,
            Amount = amount,
            CommissionRate = 0.05m,
            CommissionAmount = Math.Round(amount * 0.05m, 2, MidpointRounding.AwayFromZero),
            GatewayRef = gatewayRef,
            Status = TopUpStatus.Confirmed,
        };
        db.Context.TopUps.Add(topUp);
        db.Context.SaveChanges();
        db.Context.ChangeTracker.Clear();
        return topUp;
    }

    /// <summary>Hito M1: recarga + venta + devolución reconcilian (saldo == suma de movimientos).</summary>
    [Fact]
    public async Task TopUp_Sale_Refund_reconcile()
    {
        using var db = new TestDatabase();
        var account = db.SeedAccount(initialBalance: 0m);
        var topUp = SeedTopUp(db, account.Id, 100m, "MP-1");
        var svc = NewService(db);

        await svc.ApplyTopUpAsync(topUp.Id);          // +100
        await svc.ChargeSaleAsync(account.Id, 30m, "VENTA-1", Operator); // -30
        await svc.RefundAsync(account.Id, 10m, "DEV-1", Operator);        // +10

        var balance = await db.NewContext().Accounts
            .Where(a => a.Id == account.Id).Select(a => a.Balance).SingleAsync();
        // SUM(decimal) no lo traduce el proveedor SQLite (guarda decimal como TEXT); en SQL Server
        // es nativo. Sumamos en memoria para la prueba portátil.
        var amounts = await db.NewContext().BalanceMovements
            .Where(m => m.AccountId == account.Id).Select(m => m.Amount).ToListAsync();
        var movementSum = amounts.Sum();

        balance.Should().Be(80m);
        movementSum.Should().Be(80m);
        balance.Should().Be(movementSum, "el saldo siempre reconcilia con el libro mayor");
    }

    [Fact]
    public async Task ApplyTopUp_credits_full_amount_and_marks_applied()
    {
        using var db = new TestDatabase();
        var account = db.SeedAccount();
        var topUp = SeedTopUp(db, account.Id, 250m, "MP-2");
        var svc = NewService(db);

        var movement = await svc.ApplyTopUpAsync(topUp.Id);

        movement.Type.Should().Be(MovementType.TopUp);
        movement.Amount.Should().Be(250m);        // 100% al estudiante
        movement.BalanceAfter.Should().Be(250m);

        var applied = await db.NewContext().TopUps.SingleAsync(t => t.Id == topUp.Id);
        applied.AppliedLocally.Should().BeTrue();
        applied.Status.Should().Be(TopUpStatus.Applied);
        applied.AppliedAtUtc.Should().NotBeNull();
    }

    /// <summary>NFR-7: aplicar la misma recarga dos veces acredita una sola vez (idempotente).</summary>
    [Fact]
    public async Task ApplyTopUp_is_idempotent()
    {
        using var db = new TestDatabase();
        var account = db.SeedAccount();
        var topUp = SeedTopUp(db, account.Id, 100m, "MP-DUP");
        var svc = NewService(db);

        var first = await svc.ApplyTopUpAsync(topUp.Id);
        var second = await svc.ApplyTopUpAsync(topUp.Id); // no-op

        second.Id.Should().Be(first.Id, "la reaplicación devuelve el mismo asiento");

        var balance = await db.NewContext().Accounts
            .Where(a => a.Id == account.Id).Select(a => a.Balance).SingleAsync();
        var topUpMovements = await db.NewContext().BalanceMovements
            .CountAsync(m => m.AccountId == account.Id && m.Type == MovementType.TopUp);

        balance.Should().Be(100m, "solo se acredita una vez");
        topUpMovements.Should().Be(1);
    }

    /// <summary>FR-ACC-3 / NFR-1: una venta que excede el saldo se rechaza y no altera nada.</summary>
    [Fact]
    public async Task ChargeSale_rejects_when_insufficient_balance()
    {
        using var db = new TestDatabase();
        var account = db.SeedAccount(initialBalance: 20m);
        var svc = NewService(db);

        var act = () => svc.ChargeSaleAsync(account.Id, 50m, "VENTA-X", Operator);

        await act.Should().ThrowAsync<InsufficientBalanceException>();

        var balance = await db.NewContext().Accounts
            .Where(a => a.Id == account.Id).Select(a => a.Balance).SingleAsync();
        var movements = await db.NewContext().BalanceMovements.CountAsync(m => m.AccountId == account.Id);

        balance.Should().Be(20m, "un cargo rechazado no modifica el saldo");
        movements.Should().Be(0, "no se registra asiento para un cargo rechazado");
    }

    [Fact]
    public async Task ChargeSale_allows_overdraft_within_limit()
    {
        using var db = new TestDatabase();
        var account = db.SeedAccount(initialBalance: 0m, overdraftLimit: 100m);
        var svc = NewService(db);

        await svc.ChargeSaleAsync(account.Id, 50m, "VENTA-OD", Operator);

        var balance = await db.NewContext().Accounts
            .Where(a => a.Id == account.Id).Select(a => a.Balance).SingleAsync();
        balance.Should().Be(-50m);
    }

    /// <summary>
    /// Prueba secuencial de la lógica anti-doble-gasto: dos cargos donde el segundo excede el
    /// remanente. El UPDATE condicional deja pasar solo el que alcanza. (La concurrencia real
    /// contra SQL Server se valida en la Fase 5.9.)
    /// </summary>
    [Fact]
    public async Task Two_charges_cannot_overdraw_the_account()
    {
        using var db = new TestDatabase();
        var account = db.SeedAccount(initialBalance: 100m);
        var svc = NewService(db);

        await svc.ChargeSaleAsync(account.Id, 80m, "V1", Operator);        // ok -> 20
        var second = () => svc.ChargeSaleAsync(account.Id, 80m, "V2", Operator); // rechazado

        await second.Should().ThrowAsync<InsufficientBalanceException>();

        var balance = await db.NewContext().Accounts
            .Where(a => a.Id == account.Id).Select(a => a.Balance).SingleAsync();
        balance.Should().Be(20m);
    }

    [Fact]
    public async Task Adjust_applies_signed_amount_and_writes_movement()
    {
        using var db = new TestDatabase();
        var account = db.SeedAccount(initialBalance: 100m);
        var svc = NewService(db);

        await svc.AdjustAsync(account.Id, -15m, "Corrección de conteo", Operator);

        var balance = await db.NewContext().Accounts
            .Where(a => a.Id == account.Id).Select(a => a.Balance).SingleAsync();
        var movement = await db.NewContext().BalanceMovements
            .SingleAsync(m => m.AccountId == account.Id && m.Type == MovementType.Adjustment);

        balance.Should().Be(85m);
        movement.Amount.Should().Be(-15m);
        movement.BalanceAfter.Should().Be(85m);
    }
}
