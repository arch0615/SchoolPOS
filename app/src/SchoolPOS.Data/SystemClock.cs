using SchoolPOS.Domain.Abstractions;

namespace SchoolPOS.Data;

/// <summary>Reloj de sistema (hora real UTC).</summary>
public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
