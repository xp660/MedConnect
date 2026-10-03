using MedConnect.Domain.Entities;

namespace MedConnect.Application.Abstractions;

public interface IUserRepository
{
    /// <summary>
    /// Login 專用：一次查詢同時取回 User 與它對應的 Patient id（Domain 的 aggregate 之間只用 Id 互相參照，
    /// 沒有 navigation property，所以 join 在 Infrastructure 做）。唯讀，不追蹤。
    /// 查無此 email 回傳 null。
    /// </summary>
    Task<UserWithPatientId?> GetWithPatientIdByEmailAsync(string email, CancellationToken cancellationToken);
}

/// <summary>
/// PatientId 為 null 代表「有這個 User、卻沒有對應的 Patient 記錄」——這是資料不一致的 bug，
/// 不是使用者可控的輸入錯誤，處理方式見 LoginHandler。
/// </summary>
public sealed record UserWithPatientId(User User, long? PatientId);
