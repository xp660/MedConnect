namespace MedConnect.Domain.Entities;

public class Patient
{
    public long Id { get; private set; }
    public long UserId { get; private set; }
    public string FullName { get; private set; } = null!;
    public DateTime CreatedAtUtc { get; private set; }

    private Patient()
    {
        // EF Core
    }

    public Patient(long userId, string fullName, DateTimeOffset now)
    {
        UserId = userId;
        FullName = fullName;
        CreatedAtUtc = now.UtcDateTime;
    }
}
