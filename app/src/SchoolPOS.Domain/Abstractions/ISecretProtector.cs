namespace SchoolPOS.Domain.Abstractions;

/// <summary>
/// Cifra secretos en reposo (tokens OAuth de la pasarela). Sin esto, una lectura de la base de
/// datos de la nube basta para cobrar a nombre de cualquier escuela conectada.
/// </summary>
public interface ISecretProtector
{
    /// <summary>Cifra un valor para guardarlo. El resultado se puede almacenar como texto.</summary>
    string Protect(string plaintext);

    /// <summary>
    /// Descifra un valor guardado. Devuelve <c>null</c> si no se puede descifrar (llave rotada o
    /// perdida) — el llamador debe tratarlo como "no conectado" y pedir un nuevo OAuth, nunca
    /// propagar la excepción.
    /// </summary>
    string? Unprotect(string protectedValue);
}
