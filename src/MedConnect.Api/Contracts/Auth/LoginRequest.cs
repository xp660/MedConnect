namespace MedConnect.Api.Contracts.Auth;

public sealed record LoginRequest(string Email, string Password)
{
    // record 預設的 ToString() 會把每個屬性原樣印出來，包含明文密碼。ASP.NET Core 的 MVC 在 Trace 層級
    // 會記「Executing action method ... with arguments (...)」，直接用 ToString() 輸出參數——
    // 一旦有人把 Microsoft.AspNetCore 的日誌層級調到 Trace 排查問題，密碼就寫進日誌了
    // （MedConnect.IntegrationTests 的敏感資料測試在 Verbose 下實際抓到過）。所以這裡遮蔽。
    public override string ToString() => "LoginRequest { Email = ***, Password = *** }";
}
