using SchoolPOS.Domain.Abstractions;

namespace SchoolPOS.Data.Tests.TestSupport;

/// <summary>Reloj fijo para pruebas deterministas.</summary>
public sealed class TestClock : IClock
{
    public DateTime UtcNow { get; set; } = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
}
