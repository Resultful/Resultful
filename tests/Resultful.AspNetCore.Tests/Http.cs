using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;

namespace Resultful.AspNetCore.Tests;

public sealed record Response(int Status, string? ContentType, string Text)
{
    public JsonElement Body => JsonElement.Parse(Text);
    public bool HasBody => Text.Length > 0;
    public string? Get(string name) => Body.TryGetProperty(name, out var p) && p.ValueKind != JsonValueKind.Null ? p.ToString() : null;
}

/// <summary>Executes an IResult against a DefaultHttpContext, the way the HTTP-* spec fixtures do.</summary>
public static class Http
{
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

        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
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
