// M5 (21): Resultful.Diagnostics hooks are opt-in. A failure tags an activity with its leaf type and logs its Message,
// never the leaf's own properties.
// spec: uses Resultful.Diagnostics
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Resultful;
using Resultful.Diagnostics;
using Resultful.Spec.Support;

public sealed record CardDeclined(string CardNumber) : Error("the card was declined");

public sealed class ListLogger : ILogger
{
    public List<string> Lines { get; } = new();
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Lines.Add(formatter(state, exception));
}

public static class Cases
{
    [SpecCase]
    public static void A_failure_is_tagged_and_logged_without_its_data()
    {
        using var activity = new Activity("charge").Start();
        var failed = Result.Fail<int>(new CardDeclined("4111111111111111"));
        failed.RecordFailure(activity);
        Check.Equal(ActivityStatusCode.Error, activity.Status, "status");
        Check.Equal("CardDeclined", activity.GetTagItem(ResultfulDiagnostics.ErrorTypeTag) as string, "tag");

        var logger = new ListLogger();
        failed.LogFailure(logger, LogLevel.Warning);
        Check.True(logger.Lines.Count == 1 && logger.Lines[0].Contains("the card was declined", StringComparison.Ordinal), "logged");
        Check.False(logger.Lines[0].Contains("4111", StringComparison.Ordinal), "no leaf properties");

        Result.Ok(1).LogFailure(logger, LogLevel.Warning);
        Check.Equal(1, logger.Lines.Count, "a success logs nothing");
    }
}
