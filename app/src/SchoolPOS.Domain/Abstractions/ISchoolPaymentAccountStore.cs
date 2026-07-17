using SchoolPOS.Domain.Entities;

namespace SchoolPOS.Domain.Abstractions;

/// <summary>Persistencia de la cuenta de pago (OAuth) conectada por cada escuela.</summary>
public interface ISchoolPaymentAccountStore
{
    Task<SchoolPaymentAccount?> GetAsync(Guid schoolId, CancellationToken ct = default);

    /// <summary>Crea o actualiza la cuenta de pago de la escuela (upsert por SchoolId + Provider).</summary>
    Task SaveAsync(
        Guid schoolId, string provider, string providerUserId, string accessToken,
        string? refreshToken, DateTime expiresAtUtc, CancellationToken ct = default);
}
