using Testcontainers.MySql;

namespace MedConnect.IntegrationTests.Infrastructure;

/// <summary>
/// 整合測試用的 MySQL 容器「定義」只有這一份：image、資料庫名稱、帳號、密碼。
/// MySqlContainerFixture（handler 層、共用一個容器的測試）與 ApiWebApplicationFactory（HTTP pipeline 測試）
/// 都從這裡建容器，CancelAppointmentTests 也引用這裡的密碼（Testcontainers 會把同一組密碼設成 root 密碼）。
///
/// 只共用「定義」，不共用「容器實例」：兩個 fixture 仍然各自啟動自己的容器。它們關注的層級不同，
/// 共用同一個資料庫只會讓兩組測試的資料互相干擾，換來的「省一次約 15 秒的啟動時間」不值得
/// （理由詳見 ApiWebApplicationFactory 的類別註解）。
/// </summary>
public static class MySqlTestContainer
{
    // §8.6：固定 image tag，避免 latest 造成 flaky；與 docker-compose.yml 用同一個版本，
    // 確保「手動測到的行為」跟「自動測到的行為」是同一個 MySQL 版本的行為。
    public const string Image = "mysql:8.0.39";

    public const string Database = "medconnect";
    public const string Username = "medconnect";
    public const string Password = "medconnect";

    public static MySqlContainer Create() =>
        new MySqlBuilder(Image)
            .WithDatabase(Database)
            .WithUsername(Username)
            .WithPassword(Password)
            .Build();
}
