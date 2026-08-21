using SchoolPOS.Domain.Entities;

namespace SchoolPOS.Domain.Abstractions;

/// <summary>Emisión y verificación de llaves de la API de sincronización (una por escuela, revocable).</summary>
public interface ISyncApiKeyService
{
    /// <summary>
    /// Genera una llave nueva para la escuela y devuelve el valor en claro — la <b>única</b> vez que
    /// existe fuera del cliente que la recibe; solo el hash queda persistido.
    /// </summary>
    Task<string> IssueAsync(Guid schoolId, string label, CancellationToken ct = default);

    /// <summary>Llaves activas y revocadas de una escuela, más recientes primero (para la pantalla del proveedor).</summary>
    Task<IReadOnlyList<SyncApiKey>> ListAsync(Guid schoolId, CancellationToken ct = default);

    Task RevokeAsync(Guid keyId, CancellationToken ct = default);

    /// <summary>
    /// Verifica una llave en claro (formato <c>sync_&lt;id&gt;.&lt;secreto&gt;</c>) y devuelve el
    /// SchoolId si es válida y no está revocada; null en cualquier otro caso (formato inválido,
    /// llave inexistente, secreto incorrecto, revocada).
    /// </summary>
    Task<Guid?> VerifyAsync(string rawKey, CancellationToken ct = default);
}
