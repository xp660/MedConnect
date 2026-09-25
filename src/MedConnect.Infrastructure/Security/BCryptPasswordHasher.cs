using MedConnect.Application.Abstractions;

namespace MedConnect.Infrastructure.Security;

public sealed class BCryptPasswordHasher : IPasswordHasher
{
    public string Hash(string plainPassword) => BCrypt.Net.BCrypt.EnhancedHashPassword(plainPassword);

    public bool Verify(string plainPassword, string hash) => BCrypt.Net.BCrypt.EnhancedVerify(plainPassword, hash);
}
