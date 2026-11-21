using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using Resultful.CodeAnalysis;

namespace Resultful.AspNetCore.Tests;

public sealed record Order(int Id, string Name);

[ClosedHierarchy]
public abstract record OrderError : Error
{
    private protected OrderError(string message) : base(message) { }
}

public sealed record OrderNotFound(int Id) : OrderError($"order {Id} was not found");
public sealed record OrderLocked(int Id, string By) : OrderError($"order {Id} is locked by {By}");
public sealed record OrderCancelled(int Id) : OrderError($"order {Id} was cancelled");

public sealed record SaveFailed(int Id) : Error($"order {Id} was not saved");
public sealed record DbTimeout(string Query) : Error($"timed out running {Query}");

public sealed record Required(string Field) : Error($"{Field} is required");
public sealed record TooShort(string Field, int Min) : Error($"{Field} needs at least {Min} characters");
public sealed record InvalidId(int Id) : Error($"ID {Id} is invalid");

// An error that holds others without being a ValidationFailed: the walk reaches them through Inner().
public sealed record BatchFailed(IReadOnlyList<Error> Items) : Error("the batch failed")
{
    public override IEnumerable<Error> Inner() => Items;
}

// An error union written by hand, as Resultful.Generators writes one: a struct that implements IErrorUnion.
public readonly struct PaymentError(Error error) : IErrorUnion
{
    public Error Error { get; } = error;
}

// A leaf property that must never reach a body or a log: Error.ToString never prints a leaf's own properties.
public sealed record CredentialRejected(string Password) : Error("the credential was rejected");

public sealed record CardDeclined() : Error("The card was declined.");
public sealed record FraudSuspected(int RiskScore) : Error($"payment blocked, risk score {RiskScore}");

public sealed record Response(int Status, string? ContentType, string Text)
{
    public JsonElement Body => JsonElement.Parse(Text);
    public bool HasBody => Text.Length > 0;
    public string? Get(string name) => Body.TryGetProperty(name, out var p) && p.ValueKind != JsonValueKind.Null ? p.ToString() : null;
    public string[] Errors(string key) => [.. Body.GetProperty("errors").GetProperty(key).EnumerateArray().Select(e => e.GetString()!)];
}

/// <summary>Executes an IResult against a DefaultHttpContext with real services: options, ProblemDetails, logging.</summary>
public static class Http
{
    public const string ServerErrorTitle = "An error occurred while processing your request.";

    public static async Task<Response> Execute(
        IResult result,
        string environment = "Production",
        Action<ResultfulHttpOptions>? configure = null,
        bool register = true,
        string path = "/orders/1",
        Action<HttpContext>? request = null,
        Action<IServiceCollection>? moreServices = null,
        LogSink? log = null)
    {
        var services = new ServiceCollection().AddLogging(b => { if (log is not null) b.AddProvider(log); });
        services.AddSingleton<IHostEnvironment>(new HostingEnvironment { EnvironmentName = environment });
        if (register)
        {
            if (configure is null) services.AddResultful(); else services.AddResultful(configure);
        }

        moreServices?.Invoke(services);

        await using var provider = services.BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = provider };
        context.Request.Method = "GET";
        context.Request.Path = path;
        context.TraceIdentifier = "trace-1";
        request?.Invoke(context);
        context.Response.Body = new MemoryStream();
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        var text = await new StreamReader(context.Response.Body).ReadToEndAsync();
        return new Response(context.Response.StatusCode, context.Response.ContentType, text);
    }

    public static void MapOrders(ResultfulHttpOptions o) =>
        o.Map<OrderNotFound>(StatusCodes.Status404NotFound).Map<OrderLocked>(StatusCodes.Status409Conflict);

    public static void ByField(ResultfulHttpOptions o) => o.ValidationKey = e => e switch
    {
        Required r => r.Field,
        TooShort t => t.Field,
        _ => "",
    };
}

/// <summary>Captures every log entry, for the 5xx logging tests.</summary>
public sealed class LogSink : ILoggerProvider
{
    public List<(string Category, LogLevel Level, EventId Id, string Message)> Entries { get; } = [];

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose() { }

    sealed class Logger(LogSink sink, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (sink.Entries) sink.Entries.Add((category, logLevel, eventId, formatter(state, exception)));
        }
    }
}
