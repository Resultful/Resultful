using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Matchmark.Testing;

/// <summary>
/// Behaviour across calls, inferred from standard HTTP in the spec (LAYERS.md): idempotency keys, conditional changes,
/// paging, work that finishes later, rate limits and deprecation. Each is checked where the spec declares it.
/// </summary>
internal sealed partial class RegressionRunner
{
    private static readonly string[] Running = ["running", "pending", "queued", "in_progress", "inprogress", "processing", "notstarted", "started", "accepted"];
    internal static readonly string[] Cursors = ["cursor", "pagetoken", "page_token", "after", "continuationtoken", "continuation_token", "starting_after"];
    private static readonly string[] Nexts = ["next", "nextcursor", "next_cursor", "nextpagetoken", "next_page_token", "continuationtoken"];
    private static readonly string[] ItemsNames = ["items", "data", "results", "values", "value"];

    private int _behaviour;

    /// <summary>The behaviours one case's response can show: idempotency, work accepted for later, deprecation.</summary>
    private async Task BehavioursAsync(RegressionCase @case, Exchange exchange, List<Finding> findings, CancellationToken cancellationToken)
    {
        var operation = @case.Operation;
        var success = exchange.Status is >= 200 and < 300;
        if (success && operation.Idempotent && @case.Kind is CaseKind.Valid or CaseKind.Example or CaseKind.Boundary)
        {
            await IdempotencyAsync(@case, exchange, findings, cancellationToken).ConfigureAwait(false);
        }

        if (exchange.Status == 202)
        {
            await PollAsync(exchange, operation, @case.Name, findings, null, cancellationToken).ConfigureAwait(false);
        }

    }

    /// <summary>The same key and body again gives the same answer; the same key with another body is refused.</summary>
    private async Task IdempotencyAsync(RegressionCase @case, Exchange first, List<Finding> findings, CancellationToken cancellationToken)
    {
        if (!options.Skip.Contains(Checks.IdempotentReplay))
        {
            var replay = await SendAsync(@case.Request, cancellationToken).ConfigureAwait(false);
            if (replay.Status != first.Status || !JsonNode.DeepEquals(Volatile(first.Json()), Volatile(replay.Json())))
            {
                Add(findings, Checks.IdempotentReplay, @case.Name, replay,
                    $"the same Idempotency-Key and body again gave {replay.Status} {RegressionReport.Excerpt(replay.Body)}, not the first answer, "
                    + $"{first.Status} {RegressionReport.Excerpt(first.Body)}: a retry after a lost response would do the work twice");
            }
        }

        if (!options.Skip.Contains(Checks.IdempotencyKeyReuse) && @case.Operation.Body is { } body
            && RequestJson(@case.Request, @case.Operation) is { } json && suite.Samples.Unique(json, body.Schema, 50_000 + _behaviour++) is { } other
            && !JsonNode.DeepEquals(json, other))
        {
            var reuse = await SendAsync(@case.Request with { Body = RequestText(@case.Request, other) }, cancellationToken).ConfigureAwait(false);
            Response(@case.Operation, reuse, requestValid: false, $"{@case.Name}, key reused with another body", findings);
            if (reuse.Status is >= 200 and < 300)
            {
                Add(findings, Checks.IdempotencyKeyReuse, @case.Name, reuse,
                    $"an Idempotency-Key reused with a different body was accepted with {reuse.Status}; it must be refused (the draft says 422)");
            }
            else if (reuse.Status != 422)
            {
                Add(findings, Checks.IdempotencyKeyReuse, @case.Name, reuse, $"a reused Idempotency-Key with a different body was refused with {reuse.Status}; the draft says 422", Severity.Warning);
            }
        }
    }

    /// <summary>After a conditional change: a new ETag, and the old one now refused with 412.</summary>
    private async Task ConditionalAsync(SpecOperation operation, Exchange change, string subject, List<Finding> findings, List<Exchange> exchanges,
        CancellationToken cancellationToken)
    {
        if (change.Status is < 200 or >= 300)
        {
            return;
        }

        var sent = change.Request.Headers.FirstOrDefault(h => string.Equals(h.Key, "If-Match", StringComparison.OrdinalIgnoreCase)).Value;
        change.Headers.TryGetValue("ETag", out var etag);
        if (!options.Skip.Contains(Checks.ETagChanges) && (etag is null || etag == sent))
        {
            Add(findings, Checks.ETagChanges, subject, change, etag is null
                ? "the change succeeded but gave no ETag, so the next change can't be made safely"
                : $"the change succeeded but the ETag is still {etag}, so a stale copy still looks current");
        }

        if (sent is not null && !options.Skip.Contains(Checks.StaleIfMatchRefused))
        {
            var stale = await SendAsync(change.Request, cancellationToken).ConfigureAwait(false);
            exchanges.Add(stale);
            Response(operation, stale, requestValid: false, $"{subject}, with the old ETag", findings);
            if (stale.Status != 412)
            {
                Add(findings, Checks.StaleIfMatchRefused, subject, stale,
                    $"the same change with the now-stale If-Match {sent} gave {stale.Status}, not 412: a concurrent change would be lost");
            }
        }
    }

    /// <summary>Polls work accepted with 202 (its Location, or the first link to a GET) until it reaches a final state.</summary>
    private async Task PollAsync(Exchange accepted, SpecOperation operation, string subject, List<Finding> findings, List<Exchange>? exchanges,
        CancellationToken cancellationToken)
    {
        if (options.Skip.Contains(Checks.OperationCompletes))
        {
            return;
        }

        RegressionRequest? poll = null;
        if (accepted.Headers.TryGetValue("Location", out var location) && Uri.TryCreate(client.BaseAddress ?? new Uri("http://localhost/"), location, out var target))
        {
            var path = "/" + (client.BaseAddress ?? new Uri("http://localhost/")).MakeRelativeUri(target).OriginalString.Split('?')[0];
            poll = Template(path, "GET") is null ? null : new RegressionRequest("GET", path, [], [], null, null);
        }

        if (poll is null && operation.Response(202)?.Links.Select(l => (Link: l, Target: suite.Spec.Operations.FirstOrDefault(o => o.Id == l.OperationId)))
                .FirstOrDefault(l => l.Target?.Method == "GET") is { Target: { } read } link)
        {
            poll = Request(link.Link, read, accepted, 60_000 + _behaviour++);
        }

        if (poll is null)
        {
            return; // nothing to poll: the spec says no more about it
        }

        for (var attempt = 1; attempt <= options.MaxPolls; attempt++)
        {
            var status = await SendAsync(poll, cancellationToken).ConfigureAwait(false);
            exchanges?.Add(status);
            if (status.Status is < 200 or >= 300)
            {
                Add(findings, Checks.OperationCompletes, subject, status, $"polling the accepted work gave {status.Status}: {RegressionReport.Excerpt(status.Body)}");
                return;
            }

            var state = (status.Json() as JsonObject)?["status"] is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;
            if (state is null || !Running.Contains(state.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant()))
            {
                return;
            }

            var wait = RetryAfter(status) ?? TimeSpan.Zero;
            await Task.Delay(wait < options.MaxRetryWait ? wait : options.MaxRetryWait, cancellationToken).ConfigureAwait(false);
        }

        Add(findings, Checks.OperationCompletes, subject, accepted, $"the accepted work was still running after {options.MaxPolls} polls");
    }

    internal static void Deprecation(Exchange exchange, string subject, List<Finding> findings)
    {
        if (!exchange.Headers.TryGetValue("Deprecation", out var deprecation))
        {
            findings.Add(new Finding(Checks.DeprecationAnnounced, Severity.Failure, subject,
                "the operation is deprecated, but the response has no Deprecation header (RFC 9745), so callers aren't told", exchange.Request.Curl(null)));
            return;
        }

        // RFC 9745: a structured-field date, @<seconds since the epoch>. RFC 8594: Sunset is an HTTP-date, never earlier.
        if (!deprecation.StartsWith('@') || !long.TryParse(deprecation[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            findings.Add(new Finding(Checks.DeprecationAnnounced, Severity.Failure, subject,
                $"Deprecation is '{deprecation}'; RFC 9745 says a date such as @1688169599", exchange.Request.Curl(null)));
        }
        else if (exchange.Headers.TryGetValue("Sunset", out var sunset) && DateTimeOffset.TryParse(sunset, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var end)
                 && end < DateTimeOffset.FromUnixTimeSeconds(seconds))
        {
            findings.Add(new Finding(Checks.DeprecationAnnounced, Severity.Failure, subject,
                $"Sunset ({sunset}) is before Deprecation ({DateTimeOffset.FromUnixTimeSeconds(seconds):R}); RFC 9745 says it can't be", exchange.Request.Curl(null)));
        }
    }

    private static TimeSpan? RetryAfter(Exchange exchange)
    {
        if (!exchange.Headers.TryGetValue("Retry-After", out var value))
        {
            return null;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            return TimeSpan.FromSeconds(Math.Max(0, seconds));
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at - DateTimeOffset.UtcNow : null;
    }

    // ---------------------------------------------------------------- paging

    /// <summary>A paged list: a GET with a cursor parameter whose response has an array and a cursor to the next page.</summary>
    private (SpecParameter Cursor, string Items, string Next, SpecParameter? Limit)? Paging(SpecOperation operation)
    {
        if (operation.Method != "GET" || operation.Parameters.FirstOrDefault(p => p.In == "query" && Cursors.Contains(p.Name.ToLowerInvariant())) is not { } cursor
            || operation.Response(200)?.Content.Values.FirstOrDefault(s => s is not null) is not { } schema)
        {
            return null;
        }

        var properties = suite.Spec.Resolve(schema)["properties"]?.AsObject();
        var next = properties?.Select(p => p.Key).FirstOrDefault(k => Nexts.Contains(k.ToLowerInvariant()));
        var items = properties?.Where(p => SchemaValidator.Types(suite.Spec.Resolve(p.Value!)).Contains("array")).Select(p => p.Key)
            .OrderBy(k => ItemsNames.Contains(k.ToLowerInvariant()) ? 0 : 1).FirstOrDefault();
        var limit = operation.Parameters.FirstOrDefault(p => p.In == "query" && p.Name.ToLowerInvariant() is "limit" or "pagesize" or "page_size" or "per_page" or "top");
        return next is null || items is null ? null : (cursor, items, next, limit);
    }

    /// <summary>Walks every page with a small limit, then checks it against one big page.</summary>
    private async Task<WorkflowResult> PagesAsync(SpecOperation operation, CancellationToken cancellationToken)
    {
        var (cursor, items, next, limit) = Paging(operation)!.Value;
        var name = $"paging {operation.Id}";
        var findings = new List<Finding>();
        var exchanges = new List<Exchange>();
        var limitSchema = limit is null ? null : suite.Spec.Resolve(limit.Schema);
        var small = Math.Max(SchemaValidator.Number(limitSchema ?? [], "minimum") ?? 1, 2);
        var big = SchemaValidator.Number(limitSchema ?? [], "maximum") ?? 1000;

        RegressionRequest Page(decimal size, string? at)
        {
            var query = new List<KeyValuePair<string, string>>();
            if (limit is not null)
            {
                query.Add(new(limit.Name, size.ToString(CultureInfo.InvariantCulture)));
            }

            if (at is not null)
            {
                query.Add(new(cursor.Name, at));
            }

            return new RegressionRequest("GET", operation.Path, query, [], null, null);
        }

        var seen = new List<string>();
        string? at = null;
        for (var page = 0; ; page++)
        {
            if (page == 100)
            {
                Add(findings, Checks.PagesComplete, name, exchanges[^1], "100 pages and still a next cursor: the pages don't end");
                return new WorkflowResult(name, exchanges, findings, null);
            }

            var exchange = await SendAsync(Page(small, at), cancellationToken).ConfigureAwait(false);
            exchanges.Add(exchange);
            Response(operation, exchange, requestValid: true, name, findings);
            if (exchange.Json() is not JsonObject body || body[items] is not JsonArray list)
            {
                return new WorkflowResult(name, exchanges, findings, $"page {page + 1} gave {exchange.Status}, with no {items} to walk");
            }

            foreach (var item in list)
            {
                var key = Key(item);
                if (seen.Contains(key))
                {
                    Add(findings, Checks.PagesComplete, name, exchange, $"{Excerpt(key)} is on two pages: walking the cursors repeats items");
                    return new WorkflowResult(name, exchanges, findings, null);
                }

                seen.Add(key);
            }

            at = body[next] is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;
            if (at is null)
            {
                break;
            }
        }

        var all = await SendAsync(Page(big, null), cancellationToken).ConfigureAwait(false);
        exchanges.Add(all);
        if (all.Json() is JsonObject whole && whole[items] is JsonArray everything && Cursor(whole[next]) is null)
        {
            var expected = everything.Select(Key).ToList();
            var missing = expected.Except(seen).ToList();
            if (missing.Count > 0 || seen.Except(expected).Any())
            {
                Add(findings, Checks.PagesComplete, name, all,
                    $"walking the pages found {seen.Count} items; one page of up to {big} has {expected.Count}"
                    + (missing.Count > 0 ? $", including {Excerpt(missing[0])}, which no page had" : string.Empty));
            }
        }

        return new WorkflowResult(name, exchanges, Noticed(findings), null);
    }

    private static string? Cursor(JsonNode? next) => next is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;

    /// <summary>An item's identity: its id when it has one, else the whole item.</summary>
    private static string Key(JsonNode? item) => (item as JsonObject)?["id"]?.ToJsonString() ?? item?.ToJsonString() ?? "null";

    private static string Excerpt(string key) => key.Length <= 80 ? key : key[..80] + "…";
}
