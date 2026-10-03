using MedConnect.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MedConnect.Infrastructure.Persistence.Repositories;

public sealed class UserRepository : IUserRepository
{
    private readonly MedConnectDbContext _dbContext;

    public UserRepository(MedConnectDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<UserWithPatientId?> GetWithPatientIdByEmailAsync(string email, CancellationToken cancellationToken)
    {
        // LEFT JOIN 而不是 INNER JOIN：「有 User、沒有 Patient」必須能跟「根本沒有這個 User」分開，
        // 前者是資料不一致的 bug（由 Handler 在驗證密碼之後才丟例外），後者是 401。
        // patients.user_id 有唯一索引（ux_patients_user），所以最多一列，不會讓同一個 User 重複出現。
        var row = await (
                from user in _dbContext.Users.AsNoTracking()
                where user.Email == email
                join patient in _dbContext.Patients.AsNoTracking() on user.Id equals patient.UserId into patients
                from patient in patients.DefaultIfEmpty()
                select new { User = user, PatientId = patient == null ? (long?)null : patient.Id })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? null : new UserWithPatientId(row.User, row.PatientId);
    }
}
