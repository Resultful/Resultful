using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Matchmark.Testing;

/// <summary>
/// Hosted sessions (docs/reference/sessions.md, decisions Q34 to Q43): a copy of the API per test session, each a twin
/// from the spec seeded for that session, with scripted answers on top. The consumer starts a session with an id it
/// chooses, which stands in for logging in: the session's token is issued in the spec's own security scheme, and a token
/// presented to another session's copy is refused. Each session counts the Arazzo pathways its traffic completed, and its
/// report pairs that with the provider's results for the same pathways. Hosting it over HTTP is
/// Matchmark.Conformance's <c>UseMatchmarkSessions</c>; this is the logic, testable without a server.
/// </summary>
public sealed partial class SessionHost
{
    private readonly string _spec;
    private readonly OpenApiSpec _parsed;
    private readonly IReadOnlyList<ArazzoDocument> _pathways;
    private readonly ConcurrentDictionary<string, Session> _sessions = new(StringComparer.Ordinal);
    private readonly SecurityScheme? _scheme;

    public SessionHost(string specJson, IEnumerable<string>? arazzoJson = null)
    {
        _spec = specJson;
        _parsed = OpenApiSpec.Parse(specJson);
        _pathways = (arazzoJson ?? []).Select(ArazzoDocument.Parse).ToList();
        _scheme = SecurityScheme.Of(_parsed);
    }

    /// <summary>How long a session lives without a request before it is removed (docs/QUESTIONS.md, question 4).</summary>
    public TimeSpan Idle { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// The real service, for sessions whose copy is <c>service</c>: requests are forwarded to it, tagged with the
    /// session's id, after the host has checked the token and any script. Null when only twins are hosted.
    /// </summary>
    public ServiceCopy? Service { get; init; }

    /// <summary>The provider's results for the pathways, from a recorded <c>matchmark regress --save</c> run, if any.</summary>
    public ProviderResults? Provider { get; set; }

    /// <summary>The pathways the spec's Arazzo documents publish, by workflow id.</summary>
    public IReadOnlyList<string> PathwayIds => _pathways.SelectMany(d => d.Workflows).Select(w => w.Id).ToList();

    /// <summary>Starts a session whose copy is a twin, or replaces it when the id exists. Returns it, and whether it was new.</summary>
    public (Session Session, bool Created) Start(string id, JsonObject? seed = null, IEnumerable<string>? pathways = null)
        => StartAsync(id, seed, pathways, SessionCopy.Twin).GetAwaiter().GetResult();

    /// <summary>
    /// Starts a session, or replaces it when the id exists. A <see cref="SessionCopy.Twin"/> is seeded with collections of
    /// resources (<c>{"/pets": [...]}</c>); a <see cref="SessionCopy.Service"/> hands the seed to the service's start hook,
    /// which creates the session's own data (a database, say), and the old one is ended first.
    /// </summary>
    public async Task<(Session Session, bool Created)> StartAsync(string id, JsonNode? seed = null, IEnumerable<string>? pathways = null,
        string copy = SessionCopy.Twin, CancellationToken cancellationToken = default)
    {
        if (!SessionId().IsMatch(id))
        {
            throw new ArgumentException($"'{id}' isn't a session id: use 1 to 63 lower-case letters, digits and hyphens, starting with a letter, so it's a valid host label.", nameof(id));
        }

        var required = (pathways ?? []).ToList();
        if (required is ["all"])
        {
            required = PathwayIds.ToList();
        }

        foreach (var unknown in required.Where(p => !PathwayIds.Contains(p)))
        {
            throw new ArgumentException($"There is no pathway '{unknown}'. The pathways are: {string.Join(", ", PathwayIds)}.", nameof(pathways));
        }

        SpecTwin? twin = null;
        switch (copy)
        {
            case SessionCopy.Twin:
                twin = new SpecTwin(_spec);
                foreach (var (collection, items) in seed as JsonObject ?? [])
                {
                    twin.Seed(collection, (items as JsonArray ?? []).OfType<JsonObject>());
                }

                break;
            case SessionCopy.Service when Service is { } service:
                if (_sessions.TryGetValue(id, out var previous) && previous.Copy == SessionCopy.Service)
                {
                    await service.End(id).ConfigureAwait(false);
                }

                await service.Start(id, seed, cancellationToken).ConfigureAwait(false);
                break;
            case SessionCopy.Service:
                throw new ArgumentException("This host has no service to give a session: its copies are twins.", nameof(copy));
            default:
                throw new ArgumentException($"'{copy}' isn't a copy: use '{SessionCopy.Twin}' or '{SessionCopy.Service}'.", nameof(copy));
        }

        var session = new Session(id, twin, _scheme?.NewToken(id), required, copy);
        var created = !_sessions.ContainsKey(id);
        _sessions[id] = session;
        return (session, created);
    }

    public Session? Find(string id)
    {
        Expire();
        return _sessions.TryGetValue(id, out var session) ? session : null;
    }

    public bool End(string id) => EndAsync(id).GetAwaiter().GetResult();

    /// <summary>Ends a session; a service session's data is ended with it.</summary>
    public async Task<bool> EndAsync(string id)
    {
        if (!_sessions.TryRemove(id, out var session))
        {
            return false;
        }

        if (session.Copy == SessionCopy.Service && Service is { } service)
        {
            await service.End(id).ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>The session a request belongs to: by the first label of its host, by header, or by path prefix.</summary>
    public static (string? Id, string Path) Route(string host, IReadOnlyDictionary<string, string> headers, string path)
    {
        var name = host.Split(':')[0];
        if (name.Contains('.', StringComparison.Ordinal) && name.Split('.')[0] is { } label && SessionId().IsMatch(label) && label != "www")
        {
            return (label, path);
        }

        if (headers.TryGetValue("X-Matchmark-Session", out var header) && !string.IsNullOrWhiteSpace(header))
        {
            return (header.Trim(), path);
        }

        return PathPrefix().Match(path) is { Success: true } match ? (match.Groups["id"].Value, "/" + match.Groups["rest"].Value) : (null, path);
    }

    /// <summary>A request to a session's copy, answered synchronously; see <see cref="AnswerAsync"/>.</summary>
    public SessionAnswer Answer(Session session, string method, string path, string queryString, IReadOnlyDictionary<string, string> headers, string? body, string? contentType)
        => AnswerAsync(session, method, path, queryString, headers, body, contentType).GetAwaiter().GetResult();

    /// <summary>
    /// A request to a session's copy: the token checked where the spec secures the operation, then a script's answer if
    /// one is due, else the copy's: the twin's, or the service's for the session's own data.
    /// </summary>
    public async Task<SessionAnswer> AnswerAsync(Session session, string method, string path, string queryString, IReadOnlyDictionary<string, string> headers,
        string? body, string? contentType, CancellationToken cancellationToken = default)
    {
        session.Touch();
        var operation = Match(method, path);
        if (operation is not null && _scheme is { } scheme && Secured(operation))
        {
            var presented = scheme.Presented(headers, queryString);
            var owner = presented is null ? null : _sessions.Values.FirstOrDefault(s => s.Token == presented);
            string? refusal = presented is null ? $"This operation needs the session's token, sent as {scheme.Describe()}."
                : owner is null ? "That token isn't any session's."
                : owner != session ? $"This token belongs to session '{owner.Id}'."
                : null;
            if (refusal is not null)
            {
                var problem = new JsonObject { ["title"] = refusal, ["status"] = 401 }.ToJsonString(Readable);
                session.Record(new SessionCall(operation.Id, method, path, 401, Scripted: false, Request(method, path, queryString, headers, body, contentType), problem));
                return new SessionAnswer(401, "application/problem+json", problem, new Dictionary<string, string>());
            }
        }

        int status;
        string? type;
        string? answer;
        IReadOnlyDictionary<string, string> answerHeaders;
        bool scripted;
        if (session.Twin is { } twin)
        {
            (status, type, answer, answerHeaders) = twin.Answer(method, path, queryString, headers, body, contentType);
            scripted = session.TakeScripted();
        }
        else if (operation is not null && session.Next(operation.Id) is { } script)
        {
            // A script answers before the service is asked, so a service session can force a documented error too.
            _ = session.TakeScripted();
            (status, type, answer, answerHeaders) = (script.Status, script.Body is null ? null : script.ContentType ?? Media(operation, script.Status),
                script.Body?.ToJsonString(), new Dictionary<string, string>());
            scripted = true;
        }
        else
        {
            (status, type, answer, answerHeaders) = await Forward(session, method, path, queryString, headers, body, contentType, cancellationToken).ConfigureAwait(false);
            scripted = false;
        }

        session.Record(new SessionCall(operation?.Id, method, path, status, scripted, Request(method, path, queryString, headers, body, contentType), answer));
        return new SessionAnswer(status, type, answer, answerHeaders);
    }

    /// <summary>The service's answer, for this session's own data: the request as sent, tagged with the session's id.</summary>
    private async Task<(int, string?, string?, IReadOnlyDictionary<string, string>)> Forward(Session session, string method, string path, string queryString,
        IReadOnlyDictionary<string, string> headers, string? body, string? contentType, CancellationToken cancellationToken)
    {
        var service = Service ?? throw new InvalidOperationException("This host has no service.");
        using var request = new HttpRequestMessage(new HttpMethod(method), path.TrimStart('/') + queryString);
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8);
            request.Content.Headers.ContentType = contentType is null ? null : new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        }

        foreach (var (name, value) in headers)
        {
            if (!name.Equals("Host", StringComparison.OrdinalIgnoreCase) && !name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase)
                && !name.Equals("X-Matchmark-Session", StringComparison.OrdinalIgnoreCase))
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }
        }

        request.Headers.TryAddWithoutValidation("X-Matchmark-Session", session.Id);
        using var response = await service.Client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var answerHeaders = response.Headers.Where(h => !h.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(h => h.Key, h => string.Join(", ", h.Value), StringComparer.OrdinalIgnoreCase);
        return ((int)response.StatusCode, response.Content.Headers.ContentType?.MediaType, text.Length == 0 ? null : text, answerHeaders);
    }

    /// <summary>The spec's operation for a request, preferring literal paths to templated ones, as the twin matches.</summary>
    private SpecOperation? Match(string method, string path)
        => _parsed.Operations.Where(o => o.Method == method).OrderBy(o => o.Path.Count(c => c == '{'))
            .FirstOrDefault(o => Regex.IsMatch(path, "^" + Regex.Replace(Regex.Escape(o.Path), @"\\\{([^}]+)}", "[^/]+") + "/?$"));

    private static string Media(SpecOperation operation, int status)
        => operation.Response(status)?.Content.Keys.FirstOrDefault() ?? "application/json";

    /// <summary>Scripts a session's answers for an operation: only a status the spec documents for it.</summary>
    public void Given(Session session, string operationId, int status, JsonNode? body, int? times)
    {
        var operation = _parsed.Operations.FirstOrDefault(o => o.Id == operationId)
                        ?? throw new ArgumentException($"The spec has no operation '{operationId}'.", nameof(operationId));
        if (operation.Response(status) is null)
        {
            throw new ArgumentException($"{operationId} doesn't document a {status}: only documented outcomes can be scripted.", nameof(status));
        }

        session.Script(operationId, new TwinAnswer(status, body?.DeepClone()), times);
    }

    /// <summary>The report: the consumer's half from this session, the provider's from the recorded run.</summary>
    public SessionReport Report(Session session)
    {
        var calls = session.Calls;
        var pathways = session.Pathways.Select(id => Coverage(id, calls)).ToList();
        var invalid = calls.Count(c => !c.Scripted && c.Status is 400 or 415 or 422 or 401);
        var documented = _parsed.Operations.SelectMany(o => o.Responses.Keys.Select(k => (o.Id, k))).Count();
        var reached = calls.Where(c => c.OperationId is not null).Select(c => (c.OperationId, c.Status)).Distinct().Count();
        var consumer = new ConsumerHalf(pathways.All(p => p.Completed) && invalid == 0, pathways, calls.Count, invalid, documented, reached);

        ProviderHalf? provider = null;
        if (Provider is { } recorded && session.Pathways.Count > 0)
        {
            var results = session.Pathways.Select(id => new ProviderPathway(id, recorded.Passed(id))).ToList();
            provider = new ProviderHalf(results.All(r => r.Passed == true), "regress", recorded.RecordedAt, results);
        }

        var complete = consumer.Passed && (session.Pathways.Count == 0 || provider?.Passed == true);
        var reasons = new List<string>();
        reasons.AddRange(pathways.Where(p => !p.Completed).Select(p => $"the consumer didn't complete {p.WorkflowId}: missing {string.Join(", ", p.Missing)}"));
        reasons.AddRange(calls.Where(c => !c.Scripted && c.Status is 400 or 415 or 422 or 401).Select(c => $"the consumer sent an invalid call: {c.Method} {c.Path} was {c.Status}"));
        if (session.Pathways.Count > 0 && provider is null)
        {
            reasons.Add("there are no provider results for the pathways: record them with matchmark regress --save, and give them to the host (--provider)");
        }

        reasons.AddRange((provider?.Pathways ?? []).Where(p => p.Passed != true)
            .Select(p => p.Passed is null ? $"the provider's results don't include {p.WorkflowId}" : $"the provider fails {p.WorkflowId}"));
        return new SessionReport(session.Id, complete, consumer, provider) { Reasons = reasons };
    }

    /// <summary>A pathway is completed when the session's calls hold its steps in order, each meeting its criteria.</summary>
    private PathwayCoverage Coverage(string workflowId, IReadOnlyList<SessionCall> calls)
    {
        var workflow = _pathways.SelectMany(d => d.Workflows).First(w => w.Id == workflowId);
        var position = 0;
        var missing = new List<string>();
        foreach (var step in workflow.Steps)
        {
            var stepId = step["stepId"]?.GetValue<string>() ?? "?";
            var operationId = step["operationId"]?.GetValue<string>() is { } reference ? reference[(reference.LastIndexOf('.') + 1)..] : null;
            var found = -1;
            for (var i = position; i < calls.Count && operationId is not null; i++)
            {
                if (calls[i].OperationId == operationId && Meets(step, calls[i]))
                {
                    found = i;
                    break;
                }
            }

            if (found < 0)
            {
                missing.Add(stepId);
                continue;
            }

            position = found + 1;
        }

        return new PathwayCoverage(workflowId, missing.Count == 0, missing);
    }

    /// <summary>
    /// A step's success criteria against one call. A criterion that refers to the workflow's inputs or to other steps
    /// ($inputs, $steps, $outputs) can't be judged from one call of the consumer's, so it is left out (question 13);
    /// with nothing left to judge, any 2xx meets the step.
    /// </summary>
    private static bool Meets(JsonObject step, SessionCall call)
    {
        var criteria = (step["successCriteria"] as JsonArray ?? []).OfType<JsonObject>()
            .Where(c => !(c.ToJsonString().Contains("$inputs", StringComparison.Ordinal) || c.ToJsonString().Contains("$steps", StringComparison.Ordinal)
                          || c.ToJsonString().Contains("$outputs", StringComparison.Ordinal)))
            .ToList();
        if (criteria.Count == 0)
        {
            return call.Status is >= 200 and < 300;
        }

        var context = new ArazzoContext([]) { Current = call.Exchange() };
        try
        {
            return criteria.All(c => Criteria.Holds(c, context));
        }
        catch (Exception e) when (e is InvalidOperationException or FormatException or JsonException)
        {
            // A criterion that refers to earlier steps' outputs can't be judged from one call alone.
            return call.Status is >= 200 and < 300;
        }
    }

    private bool Secured(SpecOperation operation)
    {
        var requirements = operation.Json["security"] as JsonArray ?? _parsed.Find("#/security") as JsonArray;
        return requirements is { Count: > 0 } && requirements.Any(r => r is JsonObject { Count: > 0 });
    }

    private static RegressionRequest Request(string method, string path, string queryString, IReadOnlyDictionary<string, string> headers, string? body, string? contentType)
        => new(method, path.TrimStart('/'), SpecTwin.Query(queryString), headers.ToList(), body, contentType);

    private void Expire()
    {
        foreach (var (id, session) in _sessions)
        {
            if (DateTimeOffset.UtcNow - session.LastUsed > Idle && _sessions.TryRemove(id, out _) && session.Copy == SessionCopy.Service && Service is { } service)
            {
                _ = service.End(id);
            }
        }
    }

    /// <summary>Problems as people read them: an apostrophe as itself, not \u0027.</summary>
    private static readonly JsonSerializerOptions Readable = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    [GeneratedRegex("^[a-z][a-z0-9-]{0,62}$")]
    private static partial Regex SessionId();

    [GeneratedRegex("^/sessions/(?<id>[a-z][a-z0-9-]{0,62})(/(?<rest>.*))?$")]
    private static partial Regex PathPrefix();

    /// <summary>The spec's security scheme: how a session's token is sent, and how one is read back off a request.</summary>
    private sealed record SecurityScheme(string Type, string? Scheme, string? In, string? Name)
    {
        public static SecurityScheme? Of(OpenApiSpec spec)
        {
            var schemes = spec.Find("#/components/securitySchemes") as JsonObject ?? [];
            var used = (spec.Find("#/security") as JsonArray ?? []).Concat(spec.Operations.SelectMany(o => o.Json["security"] as JsonArray ?? []))
                .OfType<JsonObject>().SelectMany(r => r.Select(p => p.Key)).FirstOrDefault(schemes.ContainsKey);
            if (used is null || schemes[used] is not JsonObject scheme)
            {
                return null;
            }

            return new SecurityScheme(scheme["type"]?.GetValue<string>() ?? "http", scheme["scheme"]?.GetValue<string>()?.ToLowerInvariant(),
                scheme["in"]?.GetValue<string>(), scheme["name"]?.GetValue<string>());
        }

        public SessionToken NewToken(string id)
        {
            var value = $"mm_{id}_{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12))}";
            return Type switch
            {
                "apiKey" => new SessionToken(value, In ?? "header", Name ?? "X-API-Key", value),
                "http" when Scheme == "basic" => new SessionToken(value, "header", "Authorization", "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{id}:{value}"))),
                _ => new SessionToken(value, "header", "Authorization", "Bearer " + value),
            };
        }

        /// <summary>The token a request presents, or null.</summary>
        public string? Presented(IReadOnlyDictionary<string, string> headers, string queryString)
        {
            switch (Type)
            {
                case "apiKey" when In == "query":
                    return SpecTwin.Query(queryString).FirstOrDefault(q => q.Key == Name).Value;
                case "apiKey" when In == "cookie":
                    return headers.TryGetValue("Cookie", out var cookies)
                        ? cookies.Split(';').Select(c => c.Trim().Split('=', 2)).FirstOrDefault(c => c[0] == Name) is [_, var cookie] ? cookie : null
                        : null;
                case "apiKey":
                    return headers.TryGetValue(Name ?? "X-API-Key", out var key) ? key : null;
                case "http" when Scheme == "basic":
                    if (headers.TryGetValue("Authorization", out var basic) && basic.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            return Encoding.UTF8.GetString(Convert.FromBase64String(basic[6..].Trim())).Split(':', 2) is [_, var password] ? password : null;
                        }
                        catch (FormatException)
                        {
                            return null;
                        }
                    }

                    return null;
                default:
                    return headers.TryGetValue("Authorization", out var bearer) && bearer.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? bearer[7..].Trim() : null;
            }
        }

        public string Describe() => Type switch
        {
            "apiKey" => $"the {In ?? "header"} '{Name}'",
            "http" when Scheme == "basic" => "Authorization: Basic",
            _ => "Authorization: Bearer",
        };
    }
}

/// <summary>One session: its id, its copy of the API, its token, the pathways required of it, and its calls.</summary>
public sealed class Session
{
    private readonly List<SessionCall> _calls = [];
    private readonly Dictionary<string, Queue<TwinAnswer>> _scripts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TwinAnswer> _always = new(StringComparer.Ordinal);
    private bool _scripted;

    internal Session(string id, SpecTwin? twin, SessionToken? token, IReadOnlyList<string> pathways, string copy)
    {
        Id = id;
        Twin = twin;
        Copy = copy;
        TokenInfo = token;
        Pathways = pathways;
        Touch();
    }

    public string Id { get; }

    /// <summary>The session's twin, when its copy is <see cref="SessionCopy.Twin"/>; null for a service session.</summary>
    public SpecTwin? Twin { get; }

    /// <summary>What answers this session: <see cref="SessionCopy.Twin"/> or <see cref="SessionCopy.Service"/>.</summary>
    public string Copy { get; }

    /// <summary>The session's token, or null when the spec declares no security scheme.</summary>
    public SessionToken? TokenInfo { get; }

    internal string? Token => TokenInfo?.Value;

    public IReadOnlyList<string> Pathways { get; }

    public DateTimeOffset LastUsed { get; private set; }

    public IReadOnlyList<SessionCall> Calls
    {
        get
        {
            lock (_calls)
            {
                return _calls.ToList();
            }
        }
    }

    internal void Touch() => LastUsed = DateTimeOffset.UtcNow;

    internal void Record(SessionCall call)
    {
        lock (_calls)
        {
            _calls.Add(call);
        }
    }

    /// <summary>Whether the last answer came from a script; reading it clears it.</summary>
    internal bool TakeScripted()
    {
        var scripted = _scripted;
        _scripted = false;
        return scripted;
    }

    internal void Script(string operationId, TwinAnswer answer, int? times)
    {
        lock (_scripts)
        {
            if (Twin is not null && !_scripts.ContainsKey(operationId) && !_always.ContainsKey(operationId))
            {
                Twin.On(operationId, _ => Next(operationId));
            }

            if (times is { } count)
            {
                _always.Remove(operationId);
                var queue = _scripts.TryGetValue(operationId, out var existing) ? existing : _scripts[operationId] = new Queue<TwinAnswer>();
                for (var i = 0; i < count; i++)
                {
                    queue.Enqueue(answer);
                }
            }
            else
            {
                _always[operationId] = answer;
            }
        }
    }

    internal TwinAnswer? Next(string operationId)
    {
        lock (_scripts)
        {
            var answer = _scripts.TryGetValue(operationId, out var queue) && queue.Count > 0 ? queue.Dequeue()
                : _always.TryGetValue(operationId, out var always) ? always
                : null;
            _scripted = answer is not null;
            return answer;
        }
    }
}

/// <summary>What a session's copy is: the built-in twin (an in-memory store from the spec), or the real service.</summary>
public static class SessionCopy
{
    public const string Twin = "twin";

    public const string Service = "service";
}

/// <summary>
/// The real service behind service sessions: a client that reaches it, and the hooks that create a session's own data
/// when it starts (given the session's seed) and remove it when it ends. Requests carry <c>X-Matchmark-Session</c>, which
/// <c>UseMatchmarkSessionScope</c> (Matchmark.Conformance) makes the request's session in the service.
/// </summary>
public sealed record ServiceCopy(HttpClient Client, Func<string, JsonNode?, CancellationToken, Task> Start, Func<string, Task> End);

/// <summary>A session's token: its value, and where a request sends it (header, query or cookie, by name, as written).</summary>
public sealed record SessionToken(string Value, string In, string Name, string Sent);

/// <summary>One call a session's copy answered.</summary>
public sealed record SessionCall(string? OperationId, string Method, string Path, int Status, bool Scripted, RegressionRequest Request, string? Body)
{
    internal Exchange Exchange() => new(Request, Status, "application/json", Body ?? string.Empty, new Dictionary<string, string>());
}

public sealed record SessionAnswer(int Status, string? ContentType, string? Body, IReadOnlyDictionary<string, string> Headers);

public sealed record SessionReport(string Session, bool Complete, ConsumerHalf Consumer, ProviderHalf? Provider)
{
    /// <summary>Why the run isn't complete, one reason each; empty when it is.</summary>
    public IReadOnlyList<string> Reasons { get; init; } = [];
}

public sealed record ConsumerHalf(bool Passed, IReadOnlyList<PathwayCoverage> Pathways, int Calls, int Invalid, int DocumentedOutcomes, int OutcomesReached);

public sealed record PathwayCoverage(string WorkflowId, bool Completed, IReadOnlyList<string> Missing);

public sealed record ProviderHalf(bool Passed, string Source, DateTimeOffset RecordedAt, IReadOnlyList<ProviderPathway> Pathways);

public sealed record ProviderPathway(string WorkflowId, bool? Passed);

/// <summary>A recorded regression run's workflow results, for the provider's half of a session report.</summary>
public sealed record ProviderResults(DateTimeOffset RecordedAt, IReadOnlyDictionary<string, bool> Workflows)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>Whether the provider passed a pathway: a workflow named by its id, or its title and id ("Pet Store: adopt").</summary>
    public bool? Passed(string workflowId)
        => Workflows.Where(w => w.Key == workflowId || w.Key.EndsWith(": " + workflowId, StringComparison.Ordinal)).Select(w => (bool?)w.Value).FirstOrDefault();

    public static ProviderResults Of(RegressionReport report)
        => new(DateTimeOffset.UtcNow, report.Workflows.Where(w => w.Skipped is null)
            .ToDictionary(w => w.Name, w => w.Findings.All(f => f.Severity != Severity.Failure), StringComparer.Ordinal));

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static ProviderResults FromJson(string json)
        => JsonSerializer.Deserialize<ProviderResults>(json, Json) ?? throw new ArgumentException("The provider results are empty.", nameof(json));
}
