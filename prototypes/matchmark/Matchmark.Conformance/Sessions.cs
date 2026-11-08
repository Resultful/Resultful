using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Matchmark.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Matchmark.Conformance;

/// <summary>
/// Hosts a <see cref="SessionHost"/> over HTTP (docs/reference/sessions.md): the control API at <c>/__fake/sessions</c>,
/// and every other request answered by its session's copy, found by host (<c>test1.localhost</c>), by the
/// <c>X-Matchmark-Session</c> header, or by the path prefix <c>/sessions/test1/</c>.
/// </summary>
public static class SessionHosting
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static SessionHost UseMatchmarkSessions(this IApplicationBuilder app, SessionHost host, string control = "/__fake")
    {
        app.Run(async context =>
        {
            var request = context.Request;
            var path = request.Path.Value ?? "/";
            var headers = request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);
            if (path.StartsWith(control + "/sessions", StringComparison.Ordinal))
            {
                await Control(context, host, path[(control.Length + "/sessions".Length)..].Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries)).ConfigureAwait(false);
                return;
            }

            var (id, rest) = SessionHost.Route(request.Host.Value ?? string.Empty, headers, path);
            if (id is null)
            {
                await Problem(context, 404, $"No session: start one with PUT {control}/sessions/{{id}}, then call it at {{id}}.{request.Host.Value}.").ConfigureAwait(false);
                return;
            }

            if (host.Find(id) is not { } session)
            {
                await Problem(context, 404, $"There is no session '{id}'.").ConfigureAwait(false);
                return;
            }

            var body = request.ContentLength is > 0 || request.Headers.TransferEncoding.Count > 0
                ? await new StreamReader(request.Body, Encoding.UTF8).ReadToEndAsync(context.RequestAborted).ConfigureAwait(false)
                : null;
            var answer = host.Answer(session, request.Method, rest, request.QueryString.Value ?? string.Empty, headers, body, request.ContentType?.Split(';')[0].Trim());
            context.Response.StatusCode = answer.Status;
            foreach (var (name, value) in answer.Headers)
            {
                context.Response.Headers[name] = value;
            }

            if (answer.Body is not null)
            {
                context.Response.ContentType = answer.ContentType ?? "application/json";
                await context.Response.WriteAsync(answer.Body, context.RequestAborted).ConfigureAwait(false);
            }
        });
        return host;
    }

    private static async Task Control(HttpContext context, SessionHost host, string[] parts)
    {
        var method = context.Request.Method;
        try
        {
            switch (parts)
            {
                case [var id] when method == HttpMethods.Put:
                    var start = await Read(context).ConfigureAwait(false);
                    var (session, created) = host.Start(id, start?["seed"] as JsonObject,
                        start?["pathways"] switch
                        {
                            JsonArray list => list.Select(p => p!.GetValue<string>()),
                            JsonValue all when all.GetValueKind() == JsonValueKind.String => [all.GetValue<string>()],
                            _ => null,
                        });
                    var hostName = context.Request.Host;
                    // An IP address has no subdomains: a session's own host is then {id}.localhost, which browsers resolve to loopback.
                    var name = System.Net.IPAddress.TryParse(hostName.Host, out _) ? "localhost" : hostName.Host;
                    var baseUrl = $"{context.Request.Scheme}://{id}.{name}{(hostName.Port is { } port ? ":" + port : string.Empty)}/";
                    await Write(context, created ? 201 : 200, new
                    {
                        id,
                        baseUrl,
                        header = new { name = "X-Matchmark-Session", value = id },
                        token = session.TokenInfo is { } token ? new { @in = token.In, name = token.Name, value = token.Sent } : null,
                        pathways = session.Pathways,
                    }).ConfigureAwait(false);
                    return;
                case [var id] when method == HttpMethods.Delete:
                    context.Response.StatusCode = host.End(id) ? 204 : 404;
                    return;
                case [var id, "calls"] when method == HttpMethods.Get && host.Find(id) is { } found:
                    await Write(context, 200, found.Calls.Select(c => new { c.OperationId, c.Method, c.Path, c.Status, c.Scripted })).ConfigureAwait(false);
                    return;
                case [var id, "report"] when method == HttpMethods.Get && host.Find(id) is { } reported:
                    await Write(context, 200, host.Report(reported)).ConfigureAwait(false);
                    return;
                case [var id, "given", var operationId] when method == HttpMethods.Put && host.Find(id) is { } scripted:
                    var given = await Read(context).ConfigureAwait(false) ?? [];
                    host.Given(scripted, operationId, given["status"]?.GetValue<int>() ?? 200, given["body"], given["times"]?.GetValue<int>());
                    context.Response.StatusCode = 204;
                    return;
                case [var id, ..] when host.Find(id) is null:
                    await Problem(context, 404, $"There is no session '{id}'.").ConfigureAwait(false);
                    return;
                default:
                    await Problem(context, 404, "Not a session control endpoint.").ConfigureAwait(false);
                    return;
            }
        }
        catch (Exception e) when (e is ArgumentException or JsonException or InvalidOperationException or FormatException)
        {
            // An argument's message without .NET's "(Parameter 'x')" suffix: it's for the consumer, not a debugger.
            var message = e is ArgumentException { ParamName: { } name } ? e.Message.Replace($" (Parameter '{name}')", string.Empty, StringComparison.Ordinal) : e.Message;
            await Problem(context, 400, message).ConfigureAwait(false);
        }
    }

    private static async Task<JsonObject?> Read(HttpContext context)
    {
        var text = await new StreamReader(context.Request.Body, Encoding.UTF8).ReadToEndAsync(context.RequestAborted).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text) as JsonObject;
    }

    private static Task Write(HttpContext context, int status, object value)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(JsonSerializer.Serialize(value, Json), context.RequestAborted);
    }

    private static Task Problem(HttpContext context, int status, string title)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        return context.Response.WriteAsync(new JsonObject { ["title"] = title, ["status"] = status }.ToJsonString(Json), context.RequestAborted);
    }
}
