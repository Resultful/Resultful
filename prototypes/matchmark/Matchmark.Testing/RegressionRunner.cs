using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Matchmark.Testing;

public sealed class RegressionOptions
{
    /// <summary>Follow the spec's links. Turn it off for a target that keeps no state, such as a fake.</summary>
    public bool Workflows { get; set; } = true;

    /// <summary>Which cases to run; all of them by default.</summary>
    public Func<RegressionCase, bool> Include { get; set; } = _ => true;

    /// <summary>
    /// Credentials, each a header (name and value), for the security principals the run acts as (Q47). The first is sent
    /// with every request that doesn't set that header itself; the second is the other principal the scoped trait's
    /// checks (<see cref="Checks.ScopeHolds"/>) act as. Without two, those checks are a gap the report notes.
    /// </summary>
    public IList<KeyValuePair<string, string>> Principals { get; } = new List<KeyValuePair<string, string>>();

    /// <summary>Checks not to make, by name (<see cref="Checks"/>).</summary>
    public ISet<string> Skip { get; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Inputs for Arazzo workflows, by workflowId. Inputs not given are sampled from the workflow's inputs schema.</summary>
    public IDictionary<string, JsonObject> WorkflowInputs { get; } = new Dictionary<string, JsonObject>(StringComparer.Ordinal);

    /// <summary>The longest any wait lasts (an Arazzo retry, a 429's Retry-After, a poll), whatever the API says.</summary>
    public TimeSpan MaxRetryWait { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>How many times work accepted with 202 is polled before it counts as never finishing.</summary>
    public int MaxPolls { get; set; } = 20;

    /// <summary>
    /// How many random valid requests to make for each operation, on top of the deterministic cases; none by default.
    /// Each failure is shrunk to the smallest request that still fails the same way.
    /// </summary>
    public int Random { get; set; }

    /// <summary>The seed of the random requests, so that a run can be repeated exactly.</summary>
    public int Seed { get; set; } = 1;

    /// <summary>Properties that change from call to call, so reading back what was written ignores them.</summary>
    public ISet<string> Volatile { get; } = new HashSet<string>(StringComparer.Ordinal) { "traceId" };

    /// <summary>
    /// The AsyncAPI document an Arazzo 1.1 workflow's message steps name (a source description of type asyncapi), and
    /// the bridge to its broker: a step that sends posts the message to <c>POST /{operationId}</c> (the payload the body,
    /// header parameters its headers); a step that receives polls <c>GET /{operationId}?correlationId=…</c>, which answers
    /// 200 with <c>{"payload": …, "headers": {…}}</c> once such a message has been published, 204 until then.
    /// </summary>
    public AsyncApiDocument? Events { get; set; }

    /// <summary>The bridge to the broker, for <see cref="Events"/>.</summary>
    public HttpClient? Bridge { get; set; }

    /// <summary>How long a step that receives waits, when it gives no timeout of its own.</summary>
    public TimeSpan MessageTimeout { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>Sends the suite's requests and checks every response against the spec.</summary>
internal sealed partial class RegressionRunner(RegressionSuite suite, HttpClient client, RegressionOptions options)
{
    private static readonly int[] Rejections = [400, 415, 422];

    public async Task<RegressionReport> RunAsync(CancellationToken cancellationToken)
    {
        var cases = new List<CaseResult>();
        foreach (var @case in suite.Cases.Where(options.Include))
        {
            cases.Add(await RunCaseAsync(@case, cancellationToken).ConfigureAwait(false));
        }

        if (options.Random > 0)
        {
            cases.AddRange(await RandomAsync(cancellationToken).ConfigureAwait(false));
        }

        var workflows = new List<WorkflowResult>();
        if (options.Workflows)
        {
            foreach (var workflow in suite.Workflows)
            {
                workflows.Add(await ConservedAsync(workflow, () => RunWorkflowAsync(workflow, cancellationToken), cancellationToken).ConfigureAwait(false));
            }

            foreach (var workflow in suite.Arazzo)
            {
                workflows.Add(await RunArazzoAsync(workflow, cancellationToken).ConfigureAwait(false));
            }

            foreach (var paged in suite.Spec.Operations.Where(o => Paging(o) is not null))
            {
                workflows.Add(await PagesAsync(paged, cancellationToken).ConfigureAwait(false));
            }

            foreach (var (operation, parameter) in suite.Spec.Operations.SelectMany(o => Prefixes(o).Select(p => (o, p))))
            {
                workflows.Add(await PrefixAsync(operation, parameter, cancellationToken).ConfigureAwait(false));
            }
        }

        var reached = cases.Select(c => (Id: (string?)c.Case.Operation.Id, c.Exchange.Status))
            .Concat(workflows.SelectMany(w => w.Exchanges.Select(e => (Id: Operation(e.Request)?.Id, e.Status))))
            .ToHashSet();
        var outcomes = suite.Spec.Operations
            .SelectMany(o => o.Responses.Keys.Where(k => int.TryParse(k, out _))
                .Select(status => new Outcome(o.Id, status, reached.Contains(((string?)o.Id, int.Parse(status, CultureInfo.InvariantCulture))))))
            .ToList();
        return new RegressionReport(cases, workflows, outcomes);
    }

    public async Task<CaseResult> RunCaseAsync(RegressionCase @case, CancellationToken cancellationToken)
    {
        var method = @case.Operation.Method;
        var safe = method is "GET" or "HEAD" or "QUERY";
        var before = safe && !options.Skip.Contains(Checks.SafeChangesNothing) ? await SnapshotAsync(@case.Request, cancellationToken).ConfigureAwait(false) : null;
        var totals = await TotalsBeforeAsync(@case, cancellationToken).ConfigureAwait(false);

        var exchange = await SendAsync(@case.Request, cancellationToken).ConfigureAwait(false);
        var findings = new List<Finding>();
        if (totals is not null && exchange.Status is >= 200 and < 300)
        {
            await TotalsAfterAsync(@case, exchange, totals, findings, cancellationToken).ConfigureAwait(false);
        }

        RememberConserved(@case, exchange);
        var valid = @case.Kind != CaseKind.Invalid;
        var success = exchange.Status is >= 200 and < 300;
        Response(@case.Operation, exchange, valid && (@case.Kind != CaseKind.ReadOnlySent || success), @case.Name, findings);
        if (valid && @case.Kind != CaseKind.ReadOnlySent && Rejections.Contains(exchange.Status))
        {
            Add(findings, Checks.ValidAccepted, @case.Name, exchange, $"a valid request was rejected with {exchange.Status}: {RegressionReport.Excerpt(exchange.Body)}");
        }

        if (!valid)
        {
            Rejected(@case, exchange, findings);
        }

        if (before is not null && Difference(before, await SnapshotAsync(@case.Request, cancellationToken).ConfigureAwait(false)) is { } changed)
        {
            Add(findings, Checks.SafeChangesNothing, @case.Name, exchange, $"a {method}, which must change nothing, changed {changed}");
        }

        if (success && @case.Kind is CaseKind.ReadOnlySent && exchange.Json() is JsonObject stored)
        {
            foreach (var (name, sent) in @case.ReadOnlySent.Where(s => JsonNode.DeepEquals(stored[s.Key], s.Value)))
            {
                Add(findings, Checks.ReadOnlyIgnored, @case.Name, exchange, $"'{name}' is read-only, but the {sent.ToJsonString()} sent came back: the server stored it");
            }
        }

        if (success && @case.Kind is CaseKind.Valid or CaseKind.Example or CaseKind.Boundary && method is "PUT" or "DELETE"
            && !options.Skip.Contains(Checks.IdempotentTwice))
        {
            var once = await SnapshotAsync(@case.Request, cancellationToken).ConfigureAwait(false);
            var again = await SendAsync(@case.Request, cancellationToken).ConfigureAwait(false);
            Response(@case.Operation, again, requestValid: true, $"{@case.Name}, sent again", findings);
            if (Difference(once, await SnapshotAsync(@case.Request, cancellationToken).ConfigureAwait(false)) is { } effect)
            {
                Add(findings, Checks.IdempotentTwice, @case.Name, again, $"a second identical {method}, which must have the effect of one, changed {effect}");
            }
        }

        await BehavioursAsync(@case, exchange, findings, cancellationToken).ConfigureAwait(false);

        if (success && exchange.Headers.TryGetValue("Location", out var location) && !options.Skip.Contains(Checks.LocationReadable))
        {
            await LocationAsync(@case.Name, exchange, location, findings, cancellationToken).ConfigureAwait(false);
        }

        return new CaseResult(@case, exchange, Noticed(findings));
    }

    // ---------------------------------------------------------------- what HTTP itself promises

    /// <summary>
    /// What the API shows, for checking that a request changed it or didn't: every collection the spec can read without
    /// parameters, and the resource at <paramref name="at"/>'s path when the spec can read it.
    /// </summary>
    private async Task<List<(string Probe, int Status, JsonNode? Body)>> SnapshotAsync(RegressionRequest at, CancellationToken cancellationToken)
    {
        _probes ??= suite.Spec.Operations
            .Where(o => o.Method == "GET" && !o.Path.Contains('{', StringComparison.Ordinal) && o.Parameters.All(p => !p.Required) && o.Body is null)
            .Select(o => new RegressionRequest("GET", o.Path, [], [], null, null))
            .ToList();
        var probes = _probes.ToList();
        if (Template(at.Path, "GET") is not null && !probes.Any(p => p.Path == at.Path))
        {
            probes.Add(new RegressionRequest("GET", at.Path, [], [], null, null));
        }

        var snapshot = new List<(string, int, JsonNode?)>();
        foreach (var probe in probes)
        {
            var seen = await SendAsync(probe, cancellationToken).ConfigureAwait(false);
            snapshot.Add(($"GET /{probe.Target}", seen.Status, RegressionReport.Strip(seen.Json(), options.Volatile)));
        }

        return snapshot;
    }

    private List<RegressionRequest>? _probes;

    private static string? Difference(List<(string Probe, int Status, JsonNode? Body)> before, List<(string Probe, int Status, JsonNode? Body)> after)
    {
        // A probe that was rate limited or failed says nothing about whether anything changed.
        if (before.Concat(after).Any(p => p.Status is 429 or >= 500))
        {
            return null;
        }

        foreach (var (b, a) in before.Zip(after))
        {
            if (b.Status != a.Status)
            {
                return $"{b.Probe} from {b.Status} to {a.Status}";
            }

            if (!JsonNode.DeepEquals(b.Body, a.Body))
            {
                return $"{b.Probe} at {RegressionReport.FirstDifference(b.Body, a.Body, string.Empty) ?? "the body"}";
            }
        }

        return null;
    }

    /// <summary>The path template of the operation that answers <paramref name="method"/> on a concrete path.</summary>
    private string? Template(string path, string method)
        => suite.Spec.Operations.FirstOrDefault(o => o.Method == method && System.Text.RegularExpressions.Regex.IsMatch(path,
            "^" + System.Text.RegularExpressions.Regex.Replace(System.Text.RegularExpressions.Regex.Escape(o.Path), @"\\\{[^}]+}", "[^/]+") + "$"))?.Path;

    private async Task LocationAsync(string subject, Exchange exchange, string location, List<Finding> findings, CancellationToken cancellationToken)
    {
        var baseAddress = client.BaseAddress ?? new Uri("http://localhost/");
        if (!Uri.TryCreate(baseAddress, location, out var target) || !baseAddress.IsBaseOf(target))
        {
            return; // somewhere else: not this API's to check
        }

        var path = "/" + baseAddress.MakeRelativeUri(target).OriginalString.Split('?')[0];
        if (Template(path, "GET") is null)
        {
            return;
        }

        var read = await SendAsync(new RegressionRequest("GET", path, [], [], null, null), cancellationToken).ConfigureAwait(false);
        if (read.Status is < 200 or >= 300)
        {
            Add(findings, Checks.LocationReadable, subject, exchange, $"the Location {location} gave {read.Status} when read");
        }
    }

    private void Rejected(RegressionCase @case, Exchange exchange, List<Finding> findings)
    {
        if (exchange.Status is < 400 or >= 500)
        {
            if (exchange.Status < 400)
            {
                Add(findings, Checks.InvalidRejected, @case.Name, exchange, $"an invalid request was accepted with {exchange.Status}");
            }

            return;
        }

        if (@case.Location is not { } location)
        {
            return;
        }

        var documentedAsValidation = @case.Operation.Response(exchange.Status) is { } response
            && response.Content.Values.Any(s => s is not null && suite.Spec.Resolve(s)["properties"]?["errors"] is not null);
        if (exchange.Json()?["errors"] is JsonObject errors)
        {
            // A location can name several values (a dependency between parameters); naming any of them is enough.
            var targets = location.Split('|').Select(l => Normalize(l[(l.IndexOf('.') is var dot and >= 0 ? dot + 1 : l.Length)..])).ToList();
            var target = string.Join("' or '", targets);
            var keys = errors.Select(e => Normalize(e.Key)).ToList();
            var named = location == "body" || targets.Any(t => keys.Any(k => k == t || k.StartsWith(t + ".", StringComparison.Ordinal)
                || k.StartsWith(t + "[", StringComparison.Ordinal) || t.StartsWith(k + ".", StringComparison.Ordinal) && k.Length > 0));
            if (!named)
            {
                Add(findings, Checks.RejectionNamesTheValue, @case.Name, exchange,
                    $"the validation problem names {string.Join(", ", errors.Select(e => $"'{e.Key}'"))}, not '{target}'");
            }
        }
        else if (documentedAsValidation)
        {
            Add(findings, Checks.RejectionNamesTheValue, @case.Name, exchange,
                $"the {exchange.Status} is documented as a validation problem, but has no errors saying what was wrong: {RegressionReport.Excerpt(exchange.Body)}");
        }
    }

    /// <summary>"$.Details.LivesLeft", "details.livesLeft", "body.name" → "details.livesleft", "name".</summary>
    private static string Normalize(string key)
    {
        var k = key.Trim();
        k = k.StartsWith("$.", StringComparison.Ordinal) ? k[2..] : k == "$" ? string.Empty : k;
        return k.ToLowerInvariant();
    }

    /// <summary>The checks every response gets: no server error, and status, content type, body and headers as documented.</summary>
    private void Response(SpecOperation operation, Exchange exchange, bool requestValid, string subject, List<Finding> findings)
    {
        var documented = operation.Responses.GetValueOrDefault(exchange.Status.ToString(CultureInfo.InvariantCulture)) is not null;
        if (exchange.Status >= 500 && !documented)
        {
            Add(findings, Checks.NotAServerError, subject, exchange, $"{exchange.Status}: {RegressionReport.Excerpt(exchange.Body)}");
            return;
        }

        if (exchange.Status is >= 200 and < 300 && operation.Deprecated && !options.Skip.Contains(Checks.DeprecationAnnounced))
        {
            Deprecation(exchange, subject, findings);
        }

        if (operation.Response(exchange.Status) is not { } response)
        {
            Add(findings, Checks.StatusDocumented, subject, exchange,
                requestValid
                    ? $"{exchange.Status} is not a documented response of {operation.Id} ({string.Join(", ", operation.Responses.Keys)})"
                    : $"rejected with {exchange.Status}, which the spec doesn't document for {operation.Id}; document how it rejects bad input",
                requestValid ? Severity.Failure : Severity.Warning);
            return;
        }

        foreach (var header in response.Headers.Where(h => h.Required && !exchange.Headers.ContainsKey(h.Name)))
        {
            Add(findings, Checks.HeadersDocumented, subject, exchange, $"the {exchange.Status} has no {header.Name} header, which the spec requires");
        }

        if (response.Content.Count == 0)
        {
            if (exchange.Body.Length > 0)
            {
                Add(findings, Checks.ContentTypeDocumented, subject, exchange,
                    $"the spec documents no body for {exchange.Status}, but there is one ({exchange.ContentType}): {RegressionReport.Excerpt(exchange.Body)}");
            }

            return;
        }

        var mediaType = exchange.ContentType?.Split(';')[0].Trim();
        var match = response.Content.Keys.FirstOrDefault(k => MediaMatches(k, mediaType));
        if (match is null)
        {
            Add(findings, Checks.ContentTypeDocumented, subject, exchange,
                $"the {exchange.Status} is {(mediaType is null ? "without a content type" : mediaType)}; the spec documents {string.Join(", ", response.Content.Keys)}"
                + (exchange.Body.Length == 0 ? " and a body, but there is none" : string.Empty));
            return;
        }

        if (response.ItemSchemas.TryGetValue(match, out var itemSchema))
        {
            Items(exchange, match, itemSchema, subject, findings);
            return;
        }

        if (response.Content[match] is { } schema && OpenApiSpec.IsJson(match))
        {
            var json = exchange.Json();
            if (json is null && exchange.Body.Length > 0)
            {
                Add(findings, Checks.BodyConforms, subject, exchange, $"the body isn't JSON: {RegressionReport.Excerpt(exchange.Body)}");
                return;
            }

            foreach (var error in suite.Validator.Validate(json, schema, direction: Direction.Response).Take(10))
            {
                Add(findings, Checks.BodyConforms, subject, exchange, $"the {exchange.Status} body breaks its schema: {error}");
            }

            if (exchange.Status is >= 200 and < 300)
            {
                Invariants(operation, exchange, json, schema, subject, findings);
            }

            foreach (var leak in suite.Validator.WriteOnlyLeaks(json, schema))
            {
                Add(findings, Checks.WriteOnlyHidden, subject, exchange, $"{leak} is write-only, so it must never be in a response, but the {exchange.Status} has it");
            }
        }
    }

    private static bool MediaMatches(string documented, string? actual)
    {
        if (actual is null)
        {
            return false;
        }

        if (documented == "*/*" || string.Equals(documented, actual, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return documented.EndsWith("/*", StringComparison.Ordinal)
               && actual.StartsWith(documented[..^1], StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- workflows

    public async Task<WorkflowResult> RunWorkflowAsync(RegressionWorkflow workflow, CancellationToken cancellationToken)
    {
        var findings = new List<Finding>();
        var exchanges = new List<Exchange>();
        // The start request again, with fresh unique values: the case of the same name may already have created its resource.
        var startRequest = workflow.Start.Request;
        if (workflow.Start.Operation.Idempotent)
        {
            // A new attempt, so a new Idempotency-Key: the case may have used its key already.
            startRequest = startRequest with
            {
                Headers = startRequest.Headers.Select(h => string.Equals(h.Key, "Idempotency-Key", StringComparison.OrdinalIgnoreCase)
                    ? new KeyValuePair<string, string>(h.Key, $"workflow-key-{suite.Workflows.ToList().IndexOf(workflow)}") : h).ToList(),
            };
        }

        if (workflow.Start.Operation.Body is { } startBody && RequestJson(startRequest, workflow.Start.Operation) is { } json)
        {
            startRequest = startRequest with { Body = RequestText(startRequest, suite.Samples.Unique(json, startBody.Schema, 20_000 + suite.Workflows.ToList().IndexOf(workflow))) };
        }

        var start = await SendAsync(startRequest, cancellationToken).ConfigureAwait(false);
        exchanges.Add(start);
        Response(workflow.Start.Operation, start, requestValid: true, workflow.Name, findings);
        if (start.Status.ToString(CultureInfo.InvariantCulture) != workflow.Status)
        {
            return new WorkflowResult(workflow.Name, exchanges, findings, $"{workflow.Start.Name} answered {start.Status}, not {workflow.Status}, so its links weren't followed");
        }

        var links = workflow.Links.Select(l => (Link: l, Operation: suite.Spec.Operations.FirstOrDefault(o => o.Id == l.OperationId))).ToList();
        foreach (var missing in links.Where(l => l.Operation is null))
        {
            findings.Add(new Finding(Checks.ResourceAvailable, Severity.Failure, workflow.Name, $"the link {missing.Link.Name} names '{missing.Link.OperationId}', which the spec doesn't have", string.Empty));
        }

        var reads = links.Where(l => l.Operation?.Method == "GET").ToList();
        var writes = links.Where(l => l.Operation?.Method is "PUT" or "PATCH" or "POST").ToList();
        var deletes = links.Where(l => l.Operation?.Method == "DELETE").ToList();
        // What was written, to read back: not after a 202, whose work finishes later, so the resource may have moved on.
        Exchange? current = start.Status == 202 ? null : start;
        var lastSeen = start;

        async Task<Exchange> Follow((SpecLink Link, SpecOperation? Operation) link, string step)
        {
            var exchange = await SendAsync(Request(link.Link, link.Operation!, start, exchanges.Count), cancellationToken).ConfigureAwait(false);
            exchanges.Add(exchange);
            Response(link.Operation!, exchange, requestValid: true, $"{workflow.Name}: {step}", findings);
            return exchange;
        }

        async Task ReadBack(string after)
        {
            foreach (var read in reads)
            {
                var exchange = await Follow(read, $"{read.Link.Name} after {after}").ConfigureAwait(false);
                if (exchange.Status is < 200 or >= 300)
                {
                    Add(findings, Checks.ResourceAvailable, $"{workflow.Name}: {read.Link.Name} after {after}", exchange,
                        $"following {read.Link.Name} gave {exchange.Status}, not the resource: {RegressionReport.Excerpt(exchange.Body)}");
                }
                else
                {
                    if (SameSchema(lastSeen, exchange))
                    {
                        Transitions(lastSeen, exchange, $"{workflow.Name}: {read.Link.Name} after {after}", findings);
                        lastSeen = exchange;
                    }

                    if (current is null)
                    {
                        // The last write didn't return the resource (a payment returns an operation): read it, don't compare.
                        current = SameSchema(start, exchange) ? exchange : null;
                    }
                    else if (SameSchema(current, exchange) && !JsonNode.DeepEquals(Volatile(current.Json()), Volatile(exchange.Json())))
                    {
                        Add(findings, Checks.ReadAfterWrite, $"{workflow.Name}: {read.Link.Name} after {after}", exchange,
                            $"reading it back differs at {RegressionReport.FirstDifference(Volatile(current.Json()), Volatile(exchange.Json()), string.Empty)}: "
                            + $"wrote {RegressionReport.Excerpt(current.Body)}, read {RegressionReport.Excerpt(exchange.Body)}");
                    }
                }
            }
        }

        await ReadBack(workflow.Start.Operation.Id).ConfigureAwait(false);
        foreach (var write in writes)
        {
            var exchange = await Follow(write, write.Link.Name).ConfigureAwait(false);
            if (exchange.Status is < 200 or >= 300)
            {
                Add(findings, Checks.ResourceAvailable, $"{workflow.Name}: {write.Link.Name}", exchange, $"following {write.Link.Name} gave {exchange.Status}: {RegressionReport.Excerpt(exchange.Body)}");
                continue;
            }

            current = SameSchema(start, exchange) && exchange.Status != 202 ? exchange : null;
            if (SameSchema(start, exchange))
            {
                Transitions(lastSeen, exchange, $"{workflow.Name}: {write.Link.Name}", findings);
                lastSeen = exchange;
            }

            if (write.Operation!.Conditional)
            {
                await ConditionalAsync(write.Operation, exchange, $"{workflow.Name}: {write.Link.Name}", findings, exchanges, cancellationToken).ConfigureAwait(false);
            }

            if (exchange.Status == 202)
            {
                await PollAsync(exchange, write.Operation, $"{workflow.Name}: {write.Link.Name}", findings, exchanges, cancellationToken).ConfigureAwait(false);
            }

            await ReadBack(write.Link.Name).ConfigureAwait(false);
        }

        foreach (var delete in deletes)
        {
            var exchange = await Follow(delete, delete.Link.Name).ConfigureAwait(false);
            if (exchange.Status is < 200 or >= 300)
            {
                Add(findings, Checks.ResourceAvailable, $"{workflow.Name}: {delete.Link.Name}", exchange, $"following {delete.Link.Name} gave {exchange.Status}: {RegressionReport.Excerpt(exchange.Body)}");
                continue;
            }

            foreach (var read in reads)
            {
                var gone = await Follow(read, $"{read.Link.Name} after {delete.Link.Name}").ConfigureAwait(false);
                if (gone.Status is not (404 or 410))
                {
                    Add(findings, Checks.DeletedIsGone, $"{workflow.Name}: {read.Link.Name} after {delete.Link.Name}", gone,
                        $"after {delete.Link.Name} succeeded, {read.Link.Name} gave {gone.Status}, not 404 or 410: {RegressionReport.Excerpt(gone.Body)}");
                }
            }
        }

        return new WorkflowResult(workflow.Name, exchanges, Noticed(findings), null);
    }

    private JsonNode? Volatile(JsonNode? node) => RegressionReport.Strip(node, options.Volatile);

    /// <summary>Both responses have the same documented schema ($ref), so one is a representation of what the other is.</summary>
    private bool SameSchema(Exchange a, Exchange b)
    {
        string? Schema(Exchange e) => Operation(e.Request)?.Response(e.Status)?.Content.Values.Select(OpenApiSpec.RefName).FirstOrDefault(n => n is not null);
        return Schema(a) is { } x && x == Schema(b);
    }

    /// <summary>The operation a request is to: its method and path, the most literal path winning (/orders/export over /orders/{id}).</summary>
    private SpecOperation? Operation(RegressionRequest request)
        => suite.Spec.Operations.Where(o => o.Method == request.Method).OrderBy(o => o.Path.Count(c => c == '{'))
            .FirstOrDefault(o => Template(o.Path).IsMatch(request.Path));

    private static System.Text.RegularExpressions.Regex Template(string path)
        => new("^" + System.Text.RegularExpressions.Regex.Replace(System.Text.RegularExpressions.Regex.Escape(path), @"\\\{([^}]+)}", "(?<$1>[^/]+)") + "$");

    /// <summary>
    /// One exchange, checked as every response in a run is: documented status, content type, body and headers, and the
    /// invariants the spec declares. For a server's own traffic, where whether the request was valid isn't known: a
    /// 4xx the spec doesn't document is a warning, not a failure. A request to no operation in the spec is a failure.
    /// </summary>
    public IReadOnlyList<Finding> Check(Exchange exchange)
    {
        var findings = new List<Finding>();
        if (Operation(exchange.Request) is not { } operation)
        {
            Add(findings, Checks.StatusDocumented, $"{exchange.Request.Method} {exchange.Request.Path}", exchange,
                $"the spec has no operation for {exchange.Request.Method} {exchange.Request.Path}");
            return findings;
        }

        if (exchange.Request.PathParameters.Count == 0 && Template(operation.Path).Match(exchange.Request.Path) is { Success: true } match)
        {
            var parameters = match.Groups.Keys.Where(k => !int.TryParse(k, out _)).ToDictionary(k => k, k => Uri.UnescapeDataString(match.Groups[k].Value), StringComparer.Ordinal);
            exchange = exchange with { Request = exchange.Request with { PathParameters = parameters } };
        }

        Response(operation, exchange, requestValid: exchange.Status < 400, operation.Id, findings);
        return findings;
    }

    /// <summary>The request a link makes: its parameters from runtime expressions, anything else required sampled.</summary>
    private RegressionRequest Request(SpecLink link, SpecOperation target, Exchange source, int n)
    {
        var path = target.Path;
        var pathParameters = new Dictionary<string, string>(StringComparer.Ordinal);
        var query = new List<KeyValuePair<string, string>>();
        var headers = new List<KeyValuePair<string, string>>();
        foreach (var parameter in target.Parameters)
        {
            var expression = link.Parameters.FirstOrDefault(p => p.Key == parameter.Name || p.Key == $"{parameter.In}.{parameter.Name}").Value;
            string? value = expression is not null ? Evaluate(expression, source)
                : parameter.Required && suite.Samples.TrySample(parameter.Schema, false, out var sample) ? Text(sample)
                : null;
            if (value is null)
            {
                continue;
            }

            switch (parameter.In)
            {
                case "path":
                    path = path.Replace($"{{{parameter.Name}}}", Uri.EscapeDataString(value), StringComparison.Ordinal);
                    pathParameters[parameter.Name] = value;
                    break;
                case "query":
                    query.Add(new(parameter.Name, value));
                    break;
                case "header":
                    headers.Add(new(parameter.Name, value));
                    break;
            }
        }

        string? body = null;
        if (target.Body is { } spec)
        {
            body = link.RequestBody is JsonValue v && v.GetValueKind() == JsonValueKind.String && v.GetValue<string>().StartsWith('$')
                ? Evaluate(v.GetValue<string>(), source)
                : link.RequestBody?.ToJsonString()
                  ?? (suite.Samples.TrySample(spec.Schema, false, out var sample) ? suite.Samples.Unique(sample, spec.Schema, 10_000 + n)?.ToJsonString() : null);
        }

        return new RegressionRequest(target.Method, path, query, headers, body, body is null ? null : target.Body!.MediaType) { PathParameters = pathParameters };
    }

    /// <summary>An OpenAPI runtime expression ($response.body#/id, $request.path.petId...), or a constant.</summary>
    private string? Evaluate(string expression, Exchange source)
    {
        if (!expression.StartsWith('$'))
        {
            return System.Text.RegularExpressions.Regex.Replace(expression, @"\{(\$[^}]+)\}", m => Evaluate(m.Groups[1].Value, source) ?? string.Empty);
        }

        if (expression.StartsWith("$response.body", StringComparison.Ordinal))
        {
            return Text(Pointer(source.Json(), expression["$response.body".Length..].TrimStart('#')));
        }

        if (expression.StartsWith("$request.body", StringComparison.Ordinal))
        {
            return Text(Pointer(RequestJson(source.Request, Operation(source.Request)), expression["$request.body".Length..].TrimStart('#')));
        }

        var parts = expression.Split('.', 3);
        return (parts[0], parts.ElementAtOrDefault(1), parts.ElementAtOrDefault(2)) switch
        {
            ("$statusCode", _, _) => source.Status.ToString(CultureInfo.InvariantCulture),
            ("$method", _, _) => source.Request.Method,
            ("$url", _, _) => "/" + source.Request.Target,
            ("$response", "header", { } name) => source.Headers.GetValueOrDefault(name),
            ("$request", "path", { } name) => source.Request.PathParameters.GetValueOrDefault(name),
            ("$request", "query", { } name) => source.Request.Query.FirstOrDefault(q => q.Key == name).Value,
            ("$request", "header", { } name) => source.Request.Headers.FirstOrDefault(h => string.Equals(h.Key, name, StringComparison.OrdinalIgnoreCase)).Value,
            _ => null,
        };
    }

    private static JsonNode? Pointer(JsonNode? node, string pointer)
    {
        foreach (var segment in pointer.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var name = segment.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            node = node is JsonArray array && int.TryParse(name, out var index) ? array.ElementAtOrDefault(index) : node?[name];
        }

        return node;
    }

    private static string? Text(JsonNode? value) => value?.GetValueKind() switch
    {
        null => null,
        JsonValueKind.String => value.GetValue<string>(),
        JsonValueKind.Number => SchemaValidator.NumberOf(value)?.ToString(CultureInfo.InvariantCulture) ?? value.ToJsonString(),
        _ => value.ToJsonString(),
    };

    // ---------------------------------------------------------------- HTTP

    /// <summary>Sends a request; a 429 that says when to try again is waited out (capped) and retried, up to three times.</summary>
    private async Task<Exchange> SendAsync(RegressionRequest request, CancellationToken cancellationToken)
    {
        var exchange = await SendOnceAsync(request, cancellationToken).ConfigureAwait(false);
        for (var attempt = 0; attempt < 3 && exchange.Status == 429 && RetryAfter(exchange) is { } wait; attempt++)
        {
            await Task.Delay(wait < options.MaxRetryWait ? wait : options.MaxRetryWait, cancellationToken).ConfigureAwait(false);
            exchange = await SendOnceAsync(request, cancellationToken).ConfigureAwait(false);
        }

        return exchange;
    }

    /// <summary>What a send noticed on its own (a 429 without Retry-After), for whichever case or workflow it belonged to.</summary>
    private readonly List<Finding> _noticed = [];

    private List<Finding> Noticed(List<Finding> findings)
    {
        findings.AddRange(_noticed);
        _noticed.Clear();
        return findings;
    }

    private async Task<Exchange> SendOnceAsync(RegressionRequest request, CancellationToken cancellationToken)
    {
        using var message = request.ToMessage();
        if (options.Principals.Count > 0 && !request.Headers.Any(h => string.Equals(h.Key, options.Principals[0].Key, StringComparison.OrdinalIgnoreCase)))
        {
            message.Headers.Remove(options.Principals[0].Key);
            message.Headers.TryAddWithoutValidation(options.Principals[0].Key, options.Principals[0].Value);
        }

        using var response = await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var headers = response.Headers.Concat(response.Content.Headers)
            .GroupBy(h => h.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => string.Join(", ", g.SelectMany(h => h.Value)), StringComparer.OrdinalIgnoreCase);
        var exchange = new Exchange(request, (int)response.StatusCode, response.Content.Headers.ContentType?.ToString(), body, headers);
        if (exchange.Status == 429 && RetryAfter(exchange) is null)
        {
            Add(_noticed, Checks.RetryAfterGiven, request.ToString(), exchange, "a 429 without Retry-After leaves a client guessing when to try again");
        }

        return exchange;
    }

    private void Add(List<Finding> findings, string check, string subject, Exchange exchange, string message, Severity severity = Severity.Failure)
    {
        if (!options.Skip.Contains(check))
        {
            findings.Add(new Finding(check, severity, subject, message, exchange.Request.Curl(client.BaseAddress)));
        }
    }
}
