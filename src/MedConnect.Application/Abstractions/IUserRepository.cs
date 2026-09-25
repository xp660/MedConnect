using MedConnect.Domain.Entities;

namespace MedConnect.Application.Abstractions;

public interface IUserRepository
{
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken);
}
