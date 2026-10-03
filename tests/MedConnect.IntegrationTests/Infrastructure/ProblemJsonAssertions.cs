using FluentAssertions;

namespace MedConnect.IntegrationTests.Infrastructure;

public static class ProblemJsonAssertions
{
    public const string MediaType = "application/problem+json";

    /// <summary>
    /// architecture-plan.md §7：錯誤回應一律是 application/problem+json。
    ///
    /// 刻意不寫成 <c>response.Content.Headers.ContentType?.MediaType.Should().Be(...)</c>：
    /// Content-Type 整個不存在時，<c>?.</c> 會讓整條斷言被悄悄跳過、測試照樣綠燈。
    /// 這個 header 曾經長期是錯的（GlobalExceptionHandler 回的是 application/json）卻沒有任何測試發現，
    /// 就是因為沒有任何地方斷言過它。
    /// </summary>
    public static void ShouldBeProblemJson(this HttpResponseMessage response)
    {
        response.Content.Headers.ContentType.Should().NotBeNull("錯誤回應必須帶 Content-Type");
        response.Content.Headers.ContentType!.MediaType.Should().Be(MediaType);
    }
}
