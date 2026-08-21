using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Domain.Entities;

namespace SchoolPOS.Data.Services;

/// <summary>
/// Llaves de la API de sincronización. Formato en claro: <c>sync_&lt;id sin guiones&gt;.&lt;secreto
/// hex de 64 caracteres&gt;</c> — el id es público (permite ubicar la fila sin recorrer todas las
/// llaves, ya que el hash del secreto lleva sal aleatoria y no es buscable por valor) y el secreto
/// es lo único que se verifica contra el hash.
/// </summary>
public sealed class SyncApiKeyService : ISyncApiKeyService
{
    private const string Prefix = "sync_";

    private readonly SchoolDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IClock _clock;

    public SyncApiKeyService(SchoolDbContext db, IPasswordHasher hasher, IClock clock)
    {
        _db = db;
        _hasher = hasher;
        _clock = clock;
    }

    public async Task<string> IssueAsync(Guid schoolId, string label, CancellationToken ct = default)
    {
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var key = new SyncApiKey
        {
            SchoolId = schoolId,
            Label = string.IsNullOrWhiteSpace(label) ? "Agente de sincronización" : label.Trim(),
            SecretHash = _hasher.Hash(secret),
            CreatedAtUtc = _clock.UtcNow,
        };
        _db.SyncApiKeys.Add(key);
        await _db.SaveChangesAsync(ct);

        return $"{Prefix}{key.Id:N}.{secret}";
    }

    public async Task<IReadOnlyList<SyncApiKey>> ListAsync(Guid schoolId, CancellationToken ct = default) =>
        await _db.SyncApiKeys.AsNoTracking()
            .Where(k => k.SchoolId == schoolId)
            .OrderByDescending(k => k.CreatedAtUtc)
            .ToListAsync(ct);

    public async Task RevokeAsync(Guid keyId, CancellationToken ct = default)
    {
        var key = await _db.SyncApiKeys.FirstOrDefaultAsync(k => k.Id == keyId, ct);
        if (key is null || key.RevokedAtUtc is not null)
            return;
        key.RevokedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<Guid?> VerifyAsync(string rawKey, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(rawKey) || !rawKey.StartsWith(Prefix, StringComparison.Ordinal))
            return null;

        var body = rawKey[Prefix.Length..];
        var dot = body.IndexOf('.');
        if (dot < 0)
            return null;

        if (!Guid.TryParseExact(body[..dot], "N", out var keyId))
            return null;
        var secret = body[(dot + 1)..];

        var key = await _db.SyncApiKeys.FirstOrDefaultAsync(k => k.Id == keyId, ct);
        if (key is null || key.RevokedAtUtc is not null || !_hasher.Verify(secret, key.SecretHash))
            return null;

        key.LastUsedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(ct);
        return key.SchoolId;
    }
}
