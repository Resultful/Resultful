using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Matchmark.Testing;

/// <summary>
/// A test's own session on a session host (<c>matchmark host</c>, or <c>UseMatchmarkSessions</c>), for .NET tests
/// (decision Q38). <see cref="StartAsync"/> starts it; <see cref="Client"/> calls its copy, with the session's header and
/// token already set; <see cref="ReportAsync"/> asks whether the run is complete; disposing ends it.
/// The client routes by the <c>X-Matchmark-Session</c> header rather than by host, since .NET's resolver doesn't resolve
/// <c>*.localhost</c> everywhere (docs/QUESTIONS.md, question 6).
/// </summary>
public sealed class MatchmarkSession : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _control;
    private readonly string _prefix;

    private MatchmarkSession(HttpClient control, string prefix, string id, HttpClient client, string? baseUrl)
    {
        _control = control;
        _prefix = prefix;
        Id = id;
        Client = client;
        BaseUrl = baseUrl;
    }

    public string Id { get; }

    /// <summary>Calls this session's copy of the API: the host's address, with the session header and token set.</summary>
    public HttpClient Client { get; }

    /// <summary>The session's own host, for a browser or any client that resolves <c>*.localhost</c>.</summary>
    public string? BaseUrl { get; }

    /// <summary>
    /// Starts (or restarts) a session on the host <paramref name="control"/> points at. Its <paramref name="copy"/> is the
    /// built-in twin (seeded with collections of resources) or the real service (seeded however the service's start
    /// hook reads it, such as a seed scenario's name).
    /// </summary>
    public static async Task<MatchmarkSession> StartAsync(HttpClient control, string id, object? seed = null, IEnumerable<string>? pathways = null,
        string copy = SessionCopy.Twin, string prefix = "__fake", CancellationToken cancellationToken = default)
        => await StartAsync(control, id, seed, pathways, copy, scenario: null, prefix, cancellationToken).ConfigureAwait(false);

    /// <summary>Starts a session as above, with expected calls (a scenario, as Matchmark.Fakes builds or reads one).</summary>
    public static async Task<MatchmarkSession> StartAsync(HttpClient control, string id, object? seed, IEnumerable<string>? pathways, string copy,
        Matchmark.Fakes.Scenario? scenario, string prefix = "__fake", CancellationToken cancellationToken = default)
    {
        var start = new JsonObject
        {
            ["seed"] = seed is null ? null : JsonSerializer.SerializeToNode(seed, Json),
            ["pathways"] = pathways is null ? null : JsonSerializer.SerializeToNode(pathways, Json),
            ["copy"] = copy,
            ["scenario"] = scenario is null ? null : JsonNode.Parse(scenario.ToJson()),
        };
        using var response = await control.PutAsJsonAsync($"{prefix}/sessions/{id}", start, Json, cancellationToken).ConfigureAwait(false);
        var started = await response.Content.ReadFromJsonAsync<JsonObject>(Json, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode || started is null)
        {
            throw new InvalidOperationException($"The session '{id}' couldn't start: {(int)response.StatusCode} {started?["title"]}");
        }

        var client = new HttpClient { BaseAddress = control.BaseAddress };
        client.DefaultRequestHeaders.Add("X-Matchmark-Session", id);
        if (started["token"] is JsonObject token && token["in"]?.GetValue<string>() == "header")
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation(token["name"]!.GetValue<string>(), token["value"]!.GetValue<string>());
        }

        return new MatchmarkSession(control, prefix, id, client, started["baseUrl"]?.GetValue<string>())
        {
            Credential = started["token"] is JsonObject own && own["in"]?.GetValue<string>() == "header"
                ? new KeyValuePair<string, string>(own["name"]!.GetValue<string>(), own["value"]!.GetValue<string>())
                : null,
        };
    }

    /// <summary>
    /// A session id from a test's name, for a framework fixture to name each test's session after it: lower case, runs of
    /// anything but letters and digits as one hyphen, starting with a letter, cut to fit a host label with a short hash of
    /// the whole name, so two long names that share a start still differ.
    /// </summary>
    public static string IdFor(string testName)
    {
        var id = System.Text.RegularExpressions.Regex.Replace(testName.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        id = id.Length == 0 || !char.IsAsciiLetterLower(id[0]) ? "t-" + id : id;
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(testName)))[..8];
        return id.Length <= 63 ? id : id[..54].TrimEnd('-') + "-" + hash;
    }

    /// <summary>Scripts the copy's answers for an operation: only a status the spec documents for it.</summary>
    public async Task GivenAsync(string operationId, int status, object? body = null, int? times = null, CancellationToken cancellationToken = default)
    {
        using var response = await _control.PutAsJsonAsync($"{_prefix}/sessions/{Id}/given/{operationId}", new { status, body, times }, Json, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{operationId} couldn't be scripted: {await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)}");
        }
    }

    /// <summary>
    /// Mints another principal for this session (Q47), for testing the scoped trait: its credential, as the header to send
    /// it in. Requests with it reach this session's copy as someone else.
    /// </summary>
    public async Task<KeyValuePair<string, string>> AddPrincipalAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _control.PostAsync($"{_prefix}/sessions/{Id}/principals", null, cancellationToken).ConfigureAwait(false);
        var minted = await response.Content.ReadFromJsonAsync<JsonObject>(Json, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode || minted?["token"] is not JsonObject token || token["in"]?.GetValue<string>() != "header")
        {
            throw new InvalidOperationException($"No principal could be minted for session '{Id}': {(int)response.StatusCode} {minted?["title"]}");
        }

        return new KeyValuePair<string, string>(token["name"]!.GetValue<string>(), token["value"]!.GetValue<string>());
    }

    /// <summary>
    /// Delivers a message on this session's channel for an AsyncAPI operation (Q39). Throws, with what's wrong, when it
    /// doesn't conform to the operation's message.
    /// </summary>
    public async Task SendMessageAsync(string operationId, object? payload, object? headers = null, string? correlationId = null, CancellationToken cancellationToken = default)
    {
        using var response = await _control.PostAsJsonAsync($"{_prefix}/sessions/{Id}/messages/{operationId}", new { payload, headers, correlationId }, Json, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"The message on {operationId} wasn't accepted: {await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)}");
        }
    }

    /// <summary>The oldest message on this session's channel for an operation (with the correlation id, if given), or null when there's none.</summary>
    public async Task<JsonObject?> ReceiveMessageAsync(string operationId, string? correlationId = null, CancellationToken cancellationToken = default)
    {
        var query = correlationId is null ? string.Empty : "?correlationId=" + Uri.EscapeDataString(correlationId);
        using var response = await _control.GetAsync($"{_prefix}/sessions/{Id}/messages/{operationId}{query}", cancellationToken).ConfigureAwait(false);
        return response.StatusCode == System.Net.HttpStatusCode.OK ? await response.Content.ReadFromJsonAsync<JsonObject>(Json, cancellationToken).ConfigureAwait(false) : null;
    }

    /// <summary>The session's own credential, as the header it's sent in; null when the spec declares no security scheme.</summary>
    public KeyValuePair<string, string>? Credential { get; private init; }

    /// <summary>The session's report: the consumer's half and the provider's, and whether the run is complete.</summary>
    public async Task<SessionReport> ReportAsync(CancellationToken cancellationToken = default)
        => await _control.GetFromJsonAsync<SessionReport>($"{_prefix}/sessions/{Id}/report", Json, cancellationToken).ConfigureAwait(false)
           ?? throw new InvalidOperationException($"The session '{Id}' gave no report.");

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        using var _ = await _control.DeleteAsync($"{_prefix}/sessions/{Id}").ConfigureAwait(false);
    }
}
