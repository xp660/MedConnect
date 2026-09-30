using BCrypt.Net;
using MedConnect.Application.Abstractions;

namespace MedConnect.Infrastructure.Security;

public sealed class BCryptPasswordHasher : IPasswordHasher
{
    public string Hash(string plainPassword) => BCrypt.Net.BCrypt.EnhancedHashPassword(plainPassword);

    public bool Verify(string plainPassword, string hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.EnhancedVerify(plainPassword, hash);
        }
        catch (Exception ex) when (ex is SaltParseException or HashInformationException)
        {
            // 資料庫裡存的雜湊值格式不合法（例如殘留的舊 placeholder 字串）時，
            // EnhancedVerify 會丟例外而不是回傳 false。呼叫端（LoginHandler）
            // 無法、也不該分辨「密碼錯」跟「雜湊壞掉」，兩者對外都必須是同一種
            // 401 INVALID_CREDENTIALS，而不是讓例外原封不動外洩成未對映的 500。
            return false;
        }
    }
}
