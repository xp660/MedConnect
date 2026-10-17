using MedConnect.IntegrationTests.Infrastructure;
using Xunit;

namespace MedConnect.IntegrationTests.Logging;

/// <summary>
/// 日誌相關的三個 HTTP 測試類別共用同一個 ApiWebApplicationFactory（也就是同一個 MySQL 容器）。
/// 每個測試仍然用 WithWebHostBuilder 建立自己的 host（各自的捕捉 sink、各自的服務覆寫），互不干擾；
/// 共用的只有資料庫，而每個測試都自己建立病人與時段，不依賴資料庫的初始內容。
/// 少起兩個容器：容器啟動是整合測試的主要耗時，CI 時間不該因為日誌測試而明顯變長。
/// </summary>
[CollectionDefinition(Name)]
public sealed class LoggingCollection : ICollectionFixture<ApiWebApplicationFactory>
{
    public const string Name = "Logging HTTP tests";
}
