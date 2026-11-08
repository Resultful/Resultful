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

    /// <summary>Starts (or restarts) a session on the host <paramref name="control"/> points at.</summary>
    public static async Task<MatchmarkSession> StartAsync(HttpClient control, string id, object? seed = null, IEnumerable<string>? pathways = null,
        string prefix = "__fake", CancellationToken cancellationToken = default)
    {
        using var response = await control.PutAsJsonAsync($"{prefix}/sessions/{id}", new { seed, pathways }, Json, cancellationToken).ConfigureAwait(false);
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

        return new MatchmarkSession(control, prefix, id, client, started["baseUrl"]?.GetValue<string>());
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
