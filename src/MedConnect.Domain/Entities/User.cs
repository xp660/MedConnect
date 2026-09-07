using MedConnect.Domain.Enums;

namespace MedConnect.Domain.Entities;

public class User
{
    public long Id { get; private set; }
    public string Email { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public UserRole Role { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    private User()
    {
        // EF Core
    }

    public User(string email, string passwordHash, UserRole role, DateTimeOffset now)
    {
        Email = email;
        PasswordHash = passwordHash;
        Role = role;
        IsActive = true;
        CreatedAtUtc = now.UtcDateTime;
    }
}
