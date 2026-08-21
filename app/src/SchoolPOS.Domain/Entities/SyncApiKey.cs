namespace SchoolPOS.Domain.Entities;

/// <summary>
/// Credencial del Sync Agent de una escuela para hablar con la API de sincronización del portal
/// (<c>/api/sync/*</c>) en vez de tener acceso directo a la base de datos. Solo se persiste el
/// hash (NFR-6, mismo patrón que las contraseñas); la llave en claro se muestra una sola vez al
/// generarla. <see cref="RevokedAtUtc"/> permite invalidar el acceso de una escuela sin tocar las
/// demás — un laptop robado en una escuela no compromete al resto.
/// </summary>
public class SyncApiKey
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SchoolId { get; set; }

    /// <summary>Descripción libre (p. ej. "Agente principal"), solo para que el proveedor identifique la llave en la lista.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Hash PBKDF2 del secreto (la mitad de la llave que viaja después del punto).</summary>
    public string SecretHash { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public DateTime? LastUsedAtUtc { get; set; }

    public bool IsActive => RevokedAtUtc is null;
}
