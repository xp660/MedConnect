using Microsoft.Extensions.Logging;

namespace MedConnect.Application.UnitTests;

/// <summary>最小的 ILogger 替身：記下層級、例外與具名參數，給不經過 Serilog 的單元式測試用。</summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    public sealed record Entry(LogLevel Level, Exception? Exception, string Message, IReadOnlyDictionary<string, object?> State);

    private readonly List<Entry> _entries = [];

    public IReadOnlyList<Entry> Entries => _entries;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var properties = state is IEnumerable<KeyValuePair<string, object?>> pairs
            ? pairs.ToDictionary(p => p.Key, p => p.Value)
            : new Dictionary<string, object?>();

        _entries.Add(new Entry(logLevel, exception, formatter(state, exception), properties));
    }
}
