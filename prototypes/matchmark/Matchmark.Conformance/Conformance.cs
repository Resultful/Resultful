using System.Text;
using System.Text.Json.Nodes;
using Matchmark.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Matchmark.Conformance;

/// <summary>What the middleware does with a response that breaks the spec.</summary>
public enum ConformanceMode
{
    /// <summary>Log it and report it to <see cref="ConformanceOptions.OnViolation"/>; the response goes out unchanged.</summary>
    Observe,

    /// <summary>Answer with a 500 problem naming each violation instead, so a test calling the server fails.</summary>
    Enforce,
}

public sealed class ConformanceOptions
{
    public ConformanceMode Mode { get; set; } = ConformanceMode.Observe;

    /// <summary>Called with each response that breaks the spec, and the findings.</summary>
    public Action<HttpContext, IReadOnlyList<Finding>>? OnViolation { get; set; }

    /// <summary>Count warnings (an undocumented 4xx, say) as violations too. Off by default.</summary>
    public bool IncludeWarnings { get; set; }

    /// <summary>Requests to check; by default every one. Use it to leave out health checks or the OpenAPI document.</summary>
    public Func<HttpContext, bool> Include { get; set; } = _ => true;
}

/// <summary>
/// Runtime conformance: every response the server gives, checked against its spec by the same checks the regression
/// engine runs (documented status, content type, body, headers, and the invariants the spec declares). The response is
/// buffered to be checked, so a stream is checked once it ends.
/// </summary>
public static class ConformanceExtensions
{
    public static IApplicationBuilder UseMatchmarkConformance(this IApplicationBuilder app, string specJson, Action<ConformanceOptions>? configure = null)
    {
        var options = new ConformanceOptions();
        configure?.Invoke(options);
        var suite = RegressionSuite.FromJson(specJson);
        var logger = app.ApplicationServices.GetService<ILoggerFactory>()?.CreateLogger("Matchmark.Conformance");
        return app.Use(async (context, next) =>
        {
            if (!options.Include(context))
            {
                await next(context).ConfigureAwait(false);
                return;
            }

            context.Request.EnableBuffering();
            var requestBody = await new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true).ReadToEndAsync(context.RequestAborted).ConfigureAwait(false);
            context.Request.Body.Position = 0;

            var original = context.Response.Body;
            await using var buffer = new MemoryStream();
            context.Response.Body = buffer;
            try
            {
                await next(context).ConfigureAwait(false);
            }
            finally
            {
                context.Response.Body = original;
            }

            var body = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
            var findings = suite.Check(Request(context.Request, requestBody), context.Response.StatusCode, context.Response.ContentType, body, Headers(context.Response))
                .Where(f => f.Severity == Severity.Failure || options.IncludeWarnings)
                .ToList();
            if (findings.Count > 0)
            {
                foreach (var finding in findings)
                {
                    if (logger is not null)
                    {
                        Broken(logger, context.Request.Method, context.Request.Path, finding, null);
                    }
                }

                options.OnViolation?.Invoke(context, findings);
                if (options.Mode == ConformanceMode.Enforce && !context.Response.HasStarted)
                {
                    await Enforce(context, findings).ConfigureAwait(false);
                    return;
                }
            }

            buffer.Position = 0;
            await buffer.CopyToAsync(original, context.RequestAborted).ConfigureAwait(false);
        });
    }

    private static readonly Action<ILogger, string, PathString, Finding, Exception?> Broken = LoggerMessage.Define<string, PathString, Finding>(
        LogLevel.Warning, new EventId(1, "ResponseBreaksSpec"), "The response to {Method} {Path} breaks the spec: {Finding}");

    private static async Task Enforce(HttpContext context, List<Finding> findings)
    {
        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";
        var problem = new JsonObject
        {
            ["title"] = "The response breaks the spec",
            ["status"] = 500,
            ["detail"] = findings[0].ToString(),
            ["violations"] = new JsonArray(findings.Select(f => (JsonNode)JsonValue.Create(f.ToString())).ToArray()),
        };
        await context.Response.WriteAsync(problem.ToJsonString(), context.RequestAborted).ConfigureAwait(false);
    }

    private static RegressionRequest Request(HttpRequest request, string body)
        => new(
            request.Method,
            request.Path.Value ?? "/",
            request.Query.SelectMany(q => q.Value.Select(v => new KeyValuePair<string, string>(q.Key, v ?? string.Empty))).ToList(),
            request.Headers.Select(h => new KeyValuePair<string, string>(h.Key, h.Value.ToString())).ToList(),
            body.Length == 0 ? null : body,
            request.ContentType);

    private static Dictionary<string, string> Headers(HttpResponse response)
        => response.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);
}
