using Microsoft.EntityFrameworkCore;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Entities;

namespace SchoolPOS.Data.Services;

/// <summary>Almacena la cuenta de pago (OAuth) conectada por cada escuela.</summary>
public sealed class SchoolPaymentAccountStore : ISchoolPaymentAccountStore
{
    private readonly SchoolDbContext _db;
    private readonly IClock _clock;

    public SchoolPaymentAccountStore(SchoolDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public Task<SchoolPaymentAccount?> GetAsync(Guid schoolId, CancellationToken ct = default) =>
        _db.SchoolPaymentAccounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.SchoolId == schoolId && a.Provider == "MercadoPago", ct);

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
        account.AccessToken = accessToken;
        account.RefreshToken = refreshToken;
        account.ExpiresAtUtc = expiresAtUtc;
        account.UpdatedAtUtc = now;

        await _db.SaveChangesAsync(ct);
    }
}
