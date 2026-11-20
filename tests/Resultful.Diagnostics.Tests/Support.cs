using Microsoft.Extensions.Logging;

namespace Resultful.Diagnostics.Tests;

public sealed record CardDeclined(string CardNumber) : Error("the card was declined");

public sealed record GatewayTimeout(string Gateway) : Error("the gateway timed out");

public sealed record Wrapper<TPayload>(TPayload Payload) : Error("wrapped") where TPayload : notnull;

public sealed record Entry(LogLevel Level, EventId EventId, string Text, IReadOnlyList<KeyValuePair<string, object?>> State, Exception? Exception);

public sealed class ListLogger(LogLevel minimum = LogLevel.Trace) : ILogger
{
    public List<Entry> Entries { get; } = [];
    public int EnabledChecks { get; private set; }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel)
    {
        EnabledChecks++;
        return logLevel >= minimum;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Add(new Entry(logLevel, eventId, formatter(state, exception), ((IEnumerable<KeyValuePair<string, object?>>)state!).ToList(), exception));
}

/// <summary>Says yes to every level, as the spec fixture's logger does, so LogLevel.None is the package's own check.</summary>
public sealed class EverythingLogger : ILogger
{
    public int Count { get; private set; }
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Count++;
}
