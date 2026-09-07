namespace MedConnect.Domain.Entities;

public class Doctor
{
    public long Id { get; private set; }
    public string FullName { get; private set; } = null!;
    public string Specialty { get; private set; } = null!;
    public DateTime CreatedAtUtc { get; private set; }

    private Doctor()
    {
        // EF Core
    }

    public Doctor(string fullName, string specialty, DateTimeOffset now)
    {
        FullName = fullName;
        Specialty = specialty;
        CreatedAtUtc = now.UtcDateTime;
    }
}
