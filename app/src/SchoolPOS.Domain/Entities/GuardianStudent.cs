namespace SchoolPOS.Domain.Entities;

/// <summary>Vínculo muchos-a-muchos entre tutor y estudiante (padres con varios hijos).</summary>
public class GuardianStudent
{
    public Guid GuardianId { get; set; }
    public Guardian Guardian { get; set; } = null!;

    public Guid StudentId { get; set; }
    public Student Student { get; set; } = null!;

    public DateTime LinkedAtUtc { get; set; }
}
