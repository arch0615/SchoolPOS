using Microsoft.EntityFrameworkCore;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Entities;

namespace SchoolPOS.Data.Services;

/// <summary>
/// Almacena la cuenta de pago (OAuth) conectada por cada escuela. Los tokens se guardan
/// <b>cifrados</b> (<see cref="ISecretProtector"/>): con muchas escuelas en una misma base de
/// datos en la nube, una sola lectura de la tabla equivaldría, en claro, a poder cobrar a nombre
/// de todas ellas.
/// </summary>
public sealed class SchoolPaymentAccountStore : ISchoolPaymentAccountStore
{
    private readonly SchoolDbContext _db;
    private readonly IClock _clock;
    private readonly ISecretProtector _protector;

    public SchoolPaymentAccountStore(SchoolDbContext db, IClock clock, ISecretProtector protector)
    {
        _db = db;
        _clock = clock;
        _protector = protector;
    }

    public async Task<SchoolPaymentAccount?> GetAsync(Guid schoolId, CancellationToken ct = default)
    {
        // AsNoTracking: la entidad devuelta se descifra en memoria y nunca se vuelve a guardar
        // desde aquí, así que los valores en claro no pueden escaparse a la base de datos.
        var account = await _db.SchoolPaymentAccounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.SchoolId == schoolId && a.Provider == "MercadoPago", ct);
        if (account is null)
            return null;

        var accessToken = _protector.Unprotect(account.AccessToken);
        if (accessToken is null)
            return null; // llave perdida: se trata como "no conectada" → la escuela reconecta por OAuth.

        account.AccessToken = accessToken;
        account.RefreshToken = account.RefreshToken is null ? null : _protector.Unprotect(account.RefreshToken);
        return account;
    }

    public async Task SaveAsync(
        Guid schoolId, string provider, string providerUserId, string accessToken,
        string? refreshToken, DateTime expiresAtUtc, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var account = await _db.SchoolPaymentAccounts
            .FirstOrDefaultAsync(a => a.SchoolId == schoolId && a.Provider == provider, ct);

        if (account is null)
        {
            account = new SchoolPaymentAccount
            {
                SchoolId = schoolId,
                Provider = provider,
                ConnectedAtUtc = now,
            };
            _db.SchoolPaymentAccounts.Add(account);
        }

        account.ProviderUserId = providerUserId;
        account.AccessToken = _protector.Protect(accessToken);
        account.RefreshToken = refreshToken is null ? null : _protector.Protect(refreshToken);
        account.ExpiresAtUtc = expiresAtUtc;
        account.UpdatedAtUtc = now;

        await _db.SaveChangesAsync(ct);
    }
}
