using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Resultful.Diagnostics.Tests;

// A leaf property that must never reach a log or a span: the sentinel tests look for it.
public sealed record CardDeclined(string CardNumber) : Error("the card was declined");

public sealed record GatewayTimeout(string Gateway) : Error("the gateway timed out");

public sealed record Wrapper<TPayload>(TPayload Payload) : Error("wrapped") where TPayload : notnull;

// An error union written by hand, as Resultful.Generators writes one: a struct that implements IErrorUnion.
public readonly struct PaymentError(Error error) : IErrorUnion
{
    public Error Error { get; } = error;
}

#if NET11_0_OR_GREATER
// A C# 15 union, written by hand as Resultful.Generators writes one.
public union ChargeError(CardDeclined, GatewayTimeout) : IErrorUnion
{
    public Error Error => Value switch { Error e => e, _ => throw new InvalidOperationException() };
}
#endif

public sealed record Entry(LogLevel Level, EventId EventId, string Text, IReadOnlyList<KeyValuePair<string, object?>> State, Exception? Exception);

/// <summary>Captures every entry, as a structured sink would see it.</summary>
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

    /// <summary>Every string the entries carry: the rendered text and each structured value, lists flattened.</summary>
    public IEnumerable<string> AllText() => Entries.SelectMany(e => e.State.SelectMany(kv => kv.Value switch
    {
        string s => [s],
        IEnumerable<string> list => list,
        null => [],
        var other => [other.ToString() ?? ""],
    }).Append(e.Text));
}

/// <summary>Says yes to every level, so LogLevel.None is the package's own check.</summary>
public sealed class EverythingLogger : ILogger
{
    public int Count { get; private set; }
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Count++;
}

/// <summary>
/// Listens to every ActivitySource, as an OpenTelemetry exporter does, and keeps each span when it stops. Each test gets
/// its own source name, so spans from tests running in parallel are told apart.
/// </summary>
public sealed class Spans : IDisposable
{
    readonly ActivityListener _listener;
    readonly List<Activity> _stopped = [];
    readonly List<Activity> _started = [];

    public Spans()
    {
        Source = new ActivitySource("Resultful.Diagnostics.Tests." + Guid.NewGuid().ToString("N"));
        _listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = a => { lock (_started) _started.Add(a); },
            ActivityStopped = a => { if (a.Source == Source) lock (_stopped) _stopped.Add(a); },
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public ActivitySource Source { get; }

    public IReadOnlyList<Activity> Stopped { get { lock (_stopped) return _stopped.ToList(); } }

    /// <summary>Every activity started while listening, from any source and any test running in parallel.</summary>
    public IReadOnlyList<Activity> Started { get { lock (_started) return _started.ToList(); } }

    public Activity Start(string name) => Source.StartActivity(name) ?? throw new InvalidOperationException("not sampled");

    public void Dispose()
    {
        _listener.Dispose();
        Source.Dispose();
    }
}
