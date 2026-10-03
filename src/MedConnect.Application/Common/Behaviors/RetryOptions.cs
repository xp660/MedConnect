namespace MedConnect.Application.Common.Behaviors;

/// <summary>
/// RetryBehavior 的重試參數。獨立成型別只為了一件事：單元測試要把延遲設成 0，整合測試要能調整延遲，
/// 不必為此等真實的時間。預設值的理由見 architecture-plan.md §0 v1.9。
/// </summary>
public sealed record RetryOptions
{
    /// <summary>首次執行之外最多再試幾次（總嘗試次數 = MaxRetries + 1）。</summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>每次重試的基礎延遲，隨重試次數線性遞增（不是指數退避）。</summary>
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromMilliseconds(25);

    /// <summary>在延遲上再加 [0, JitterRange) 的隨機值，讓同時失敗的請求不要在下一輪又同時重試。</summary>
    public TimeSpan JitterRange { get; init; } = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// delay = BaseDelay * (attempt + 1) + Random(0, JitterRange)。attempt 從 0 起算（第 1 次重試 = 0）。
    /// </summary>
    public TimeSpan GetDelay(int attempt, Random random)
    {
        var jitter = TimeSpan.FromTicks((long)(random.NextDouble() * JitterRange.Ticks));
        return BaseDelay * (attempt + 1) + jitter;
    }
}
