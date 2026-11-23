using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace CSharpApp.UnitTests.TestDoubles;

/// <summary>Records every log call with its level and rendered message.</summary>
public sealed class ListLogger<T> : ILogger<T>
{
    private readonly ConcurrentQueue<Entry> _entries = new();

    public IReadOnlyList<Entry> Entries => [.. _entries];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        _entries.Enqueue(new Entry(logLevel, formatter(state, exception)));

    /// <summary>One log call.</summary>
    public sealed record Entry(LogLevel Level, string Message);
}
