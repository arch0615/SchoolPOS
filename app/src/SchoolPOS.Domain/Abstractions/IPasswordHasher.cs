namespace SchoolPOS.Domain.Abstractions;

/// <summary>Hash y verificación de contraseñas (nunca se almacena la contraseña en claro, NFR-6).</summary>
public interface IPasswordHasher
{
    /// <summary>Devuelve un hash con sal (formato autocontenido) listo para persistir.</summary>
    string Hash(string password);

    /// <summary>Verifica una contraseña contra un hash previamente generado.</summary>
    bool Verify(string password, string hash);
}
