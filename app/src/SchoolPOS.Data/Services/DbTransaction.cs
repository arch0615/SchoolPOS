namespace SchoolPOS.Data.Services;

/// <summary>
/// Helper de transacciones componibles. Si ya hay una transacción activa en el contexto,
/// ejecuta la acción dentro de ella (transacción ambiental); si no, abre una nueva y la
/// confirma al terminar. Permite que un servicio (p. ej. ventas) envuelva varias operaciones
/// atómicas (descuento de stock + cargo a saldo + alta de venta) en una sola transacción.
/// </summary>
internal static class DbTransaction
{
    public static async Task<T> ExecuteAtomicAsync<T>(
        this SchoolDbContext db, Func<Task<T>> action, CancellationToken ct)
    {
        // Ya dentro de una transacción: participar en ella (no anidar).
        if (db.Database.CurrentTransaction is not null)
            return await action();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var result = await action();
        await tx.CommitAsync(ct); // si la acción lanza, el dispose revierte automáticamente
        return result;
    }
}
