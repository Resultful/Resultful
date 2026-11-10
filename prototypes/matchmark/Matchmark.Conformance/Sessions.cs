using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Matchmark.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

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
        app.UseWebSockets();
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

            if (context.WebSockets.IsWebSocketRequest && host.Channel(rest) is { } channel)
            {
                await Socket(context, host, session, channel).ConfigureAwait(false);
                return;
            }

            var body = request.ContentLength is > 0 || request.Headers.TransferEncoding.Count > 0
                ? await new StreamReader(request.Body, Encoding.UTF8).ReadToEndAsync(context.RequestAborted).ConfigureAwait(false)
                : null;
            var answer = await host.AnswerAsync(session, request.Method, rest, request.QueryString.Value ?? string.Empty, headers, body,
                request.ContentType?.Split(';')[0].Trim(), context.RequestAborted).ConfigureAwait(false);
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
                    var (session, created) = await host.StartAsync(id, start?["seed"],
                        start?["pathways"] switch
                        {
                            JsonArray list => list.Select(p => p!.GetValue<string>()),
                            JsonValue all when all.GetValueKind() == JsonValueKind.String => [all.GetValue<string>()],
                            _ => null,
                        },
                        start?["copy"]?.GetValue<string>() ?? SessionCopy.Twin,
                        start?["scenario"] is JsonObject scenario ? Matchmark.Fakes.Scenario.FromJson(scenario.ToJsonString()) : null,
                        context.RequestAborted).ConfigureAwait(false);
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
                        copy = session.Copy,
                    }).ConfigureAwait(false);
                    return;
                case [var id] when method == HttpMethods.Delete:
                    context.Response.StatusCode = await host.EndAsync(id).ConfigureAwait(false) ? 204 : 404;
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
                case [var id, "messages", var operationId] when method == HttpMethods.Post && host.Find(id) is { } delivering:
                    var envelope = await Read(context).ConfigureAwait(false) ?? [];
                    var errors = host.Deliver(delivering, operationId, envelope["payload"], envelope["headers"] as JsonObject, envelope["correlationId"]?.ToString());
                    if (errors.Count == 0)
                    {
                        context.Response.StatusCode = 202;
                    }
                    else
                    {
                        await Write(context, 400, new { title = $"The message doesn't conform to {operationId}'s AsyncAPI message.", status = 400, errors }).ConfigureAwait(false);
                    }

                    return;
                case [var id, "messages", var operationId] when method == HttpMethods.Get && host.Find(id) is { } receiving:
                    if (host.Receive(receiving, operationId, context.Request.Query["correlationId"].FirstOrDefault()) is { } message)
                    {
                        await Write(context, 200, new { payload = message.Payload, headers = message.Headers, correlationId = message.CorrelationId }).ConfigureAwait(false);
                    }
                    else
                    {
                        context.Response.StatusCode = 204;
                    }

                    return;
                case [var id, "principals"] when method == HttpMethods.Post && host.Find(id) is { } minting:
                    if (host.MintPrincipal(minting) is { } minted)
                    {
                        await Write(context, 201, new { token = new { @in = minted.In, name = minted.Name, value = minted.Sent } }).ConfigureAwait(false);
                    }
                    else
                    {
                        await Problem(context, 409, "The spec declares no security scheme, so a session has no principals to mint.").ConfigureAwait(false);
                    }

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

    /// <summary>
    /// A session's WebSocket on a channel (Q39): what the API sends on it is pushed, and what the client sends is the
    /// message the API receives there, checked like any other ({"payload", "headers"}, or the bare payload). A message
    /// that doesn't conform is answered on the socket with {"errors": […]}.
    /// </summary>
    private static async Task Socket(HttpContext context, SessionHost host, Session session, string channel)
    {
        using var socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
        var gate = new SemaphoreSlim(1, 1);
        async Task Send(object value)
        {
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Json));
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (socket.State == System.Net.WebSockets.WebSocketState.Open)
                {
                    await socket.SendAsync(bytes, System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                gate.Release();
            }
        }

        void Push(SessionMessage message)
        {
            if (host.Sends(message.OperationId, channel))
            {
                _ = Send(new { operation = message.OperationId, payload = message.Payload, headers = message.Headers, correlationId = message.CorrelationId });
            }
        }

        session.Delivered += Push;
        try
        {
            var buffer = new byte[64 * 1024];
            while (socket.State == System.Net.WebSockets.WebSocketState.Open && !context.RequestAborted.IsCancellationRequested)
            {
                using var frame = new MemoryStream();
                System.Net.WebSockets.WebSocketReceiveResult received;
                do
                {
                    received = await socket.ReceiveAsync(buffer, context.RequestAborted).ConfigureAwait(false);
                    frame.Write(buffer, 0, received.Count);
                }
                while (!received.EndOfMessage);

                if (received.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, null, CancellationToken.None).ConfigureAwait(false);
                    break;
                }

                if (host.Receiving(channel) is not { } operation)
                {
                    await Send(new { errors = new[] { $"The API receives nothing on {channel}." } }).ConfigureAwait(false);
                    continue;
                }

                JsonNode? message;
                try
                {
                    message = JsonNode.Parse(frame.ToArray());
                }
                catch (JsonException e)
                {
                    await Send(new { errors = new[] { "Not JSON: " + e.Message } }).ConfigureAwait(false);
                    continue;
                }

                var envelope = message is JsonObject o && o.ContainsKey("payload") ? o : null;
                var errors = host.Deliver(session, operation, envelope is null ? message : envelope["payload"], envelope?["headers"] as JsonObject, envelope?["correlationId"]?.ToString());
                if (errors.Count > 0)
                {
                    await Send(new { operation, errors }).ConfigureAwait(false);
                }
            }
        }
        catch (Exception e) when (e is System.Net.WebSockets.WebSocketException or OperationCanceledException)
        {
            // The client went away.
        }
        finally
        {
            session.Delivered -= Push;
            gate.Dispose();
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

    /// <summary>
    /// In a service behind service sessions: makes each request's session (by host, <c>X-Matchmark-Session</c> or path
    /// prefix, as the session host routes) the request's <see cref="MatchmarkSession(HttpContext)"/>, so the service can
    /// choose that session's data, such as its own database. Requests with no session have none.
    /// </summary>
    public static IApplicationBuilder UseMatchmarkSessionScope(this IApplicationBuilder app)
        => app.Use(async (context, next) =>
        {
            var headers = context.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);
            var (id, _) = SessionHost.Route(context.Request.Host.Value ?? string.Empty, headers, context.Request.Path.Value ?? "/");
            if (id is not null)
            {
                context.Items[SessionKey] = id;
            }

            await next(context).ConfigureAwait(false);
        });

    /// <summary>
    /// In a service in another process: the hooks a session host calls (ServiceCopy.Http) to start a session's data, with
    /// its seed as the body (<c>PUT /__matchmark/sessions/{id}</c>, answered 204), and to end it (<c>DELETE</c>, 204).
    /// </summary>
    public static IEndpointRouteBuilder MapMatchmarkSessionHooks(this IEndpointRouteBuilder app, Func<string, JsonNode?, CancellationToken, Task> start,
        Func<string, Task> end, string path = "/" + ServiceCopy.HooksPath)
    {
        app.MapPut(path.TrimEnd('/') + "/{id}", async (string id, HttpContext context) =>
        {
            var text = await new StreamReader(context.Request.Body, Encoding.UTF8).ReadToEndAsync(context.RequestAborted).ConfigureAwait(false);
            await start(id, string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text), context.RequestAborted).ConfigureAwait(false);
            return Results.NoContent();
        });
        app.MapDelete(path.TrimEnd('/') + "/{id}", async (string id) =>
        {
            await end(id).ConfigureAwait(false);
            return Results.NoContent();
        });
        return app;
    }

    /// <summary>The session this request belongs to, set by <see cref="UseMatchmarkSessionScope"/>; null when none.</summary>
    public static string? MatchmarkSession(this HttpContext context) => context.Items.TryGetValue(SessionKey, out var id) ? id as string : null;

    private const string SessionKey = "Matchmark.Session";
}
