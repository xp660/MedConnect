namespace MedConnect.IntegrationTests.Infrastructure;

/// <summary>
/// 讓所有整合測試共用同一個 MySQL 容器（啟動一次約 20 秒，每個 test class 各起一台不划算），
/// 同時也讓同一個 collection 內的測試「循序」執行——併發測試要斷言精確的數字，
/// 不能有別的測試同時在對同一個資料庫寫東西。
/// </summary>
[CollectionDefinition(Name)]
public class MySqlContainerCollection : ICollectionFixture<MySqlContainerFixture>
{
    public const string Name = "MySQL container";
}
