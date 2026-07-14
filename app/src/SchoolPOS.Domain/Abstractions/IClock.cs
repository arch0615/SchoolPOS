namespace SchoolPOS.Domain.Abstractions;

/// <summary>Reloj inyectable (UTC). Permite pruebas deterministas del libro mayor.</summary>
public interface IClock
{
    DateTime UtcNow { get; }
}
