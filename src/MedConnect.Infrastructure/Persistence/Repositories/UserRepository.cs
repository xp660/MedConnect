using MedConnect.Application.Abstractions;
using MedConnect.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MedConnect.Infrastructure.Persistence.Repositories;

public sealed class UserRepository : IUserRepository
{
    private readonly MedConnectDbContext _dbContext;

    public UserRepository(MedConnectDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken) =>
        _dbContext.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
}
