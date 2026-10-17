using FluentAssertions;
using MedConnect.Infrastructure;
using MedConnect.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace MedConnect.IntegrationTests.Startup;

/// <summary>
/// HS256 要求金鑰至少 256 bits，但 JwtOptions 以前只檢查「非空白」：太短的金鑰能讓 host 正常啟動，
/// 要到第一次 Login 簽章時才 500。現在改成啟動時（ValidateOnStart）就失敗。以 UTF-8「位元組數」檢查。
/// 不需要資料庫：只建 DI 容器、解析 IOptions。
/// </summary>
public class JwtOptionsValidationTests
{
    private static string Resolve(string? secretKey)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:MedConnect"] = "Server=unused;Database=unused;User=unused;Password=unused;",
            ["Jwt:Issuer"] = "issuer",
            ["Jwt:Audience"] = "audience",
        };
        if (secretKey is not null)
        {
            values["Jwt:SecretKey"] = secretKey;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();

        return provider.GetRequiredService<IOptions<JwtOptions>>().Value.SecretKey;
    }

    [Theory]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(64)]
    public void A_key_of_at_least_32_bytes_is_accepted(int length) =>
        Resolve(new string('k', length)).Should().HaveLength(length);

    [Theory]
    [InlineData(1)]
    [InlineData(16)]
    [InlineData(31)]
    public void A_key_shorter_than_32_bytes_fails_validation_without_echoing_the_key(int length)
    {
        var secret = new string('#', length); // 訊息文字裡不會出現 '#'，所以「包含」就代表洩漏

        var act = () => Resolve(secret);

        var exception = act.Should().Throw<OptionsValidationException>().Which;
        exception.Message.Should().Contain("at least 32 bytes");
        exception.Message.Should().NotContain(secret, "錯誤訊息不可包含密鑰的值或任何片段");
    }

    [Fact]
    public void The_check_counts_UTF8_bytes_not_characters()
    {
        // 11 個中文字 = 33 位元組（夠）；30 個字元的 ASCII = 30 位元組（不夠）。
        Resolve(new string('密', 11)).Should().HaveLength(11);

        var act = () => Resolve(new string('a', 30));
        act.Should().Throw<OptionsValidationException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_key_still_fails_with_the_missing_message(string? secret)
    {
        var act = () => Resolve(secret);

        act.Should().Throw<OptionsValidationException>().WithMessage("*Missing Jwt:SecretKey*");
    }
}
