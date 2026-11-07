using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Resultful.OpenApi.Testing;

public sealed class RegressionOptions
{
    /// <summary>Follow the spec's links. Turn it off for a target that keeps no state, such as a fake.</summary>
    public bool Workflows { get; set; } = true;

    /// <summary>Which cases to run; all of them by default.</summary>
    public Func<RegressionCase, bool> Include { get; set; } = _ => true;

    /// <summary>Checks not to make, by name (<see cref="Checks"/>).</summary>
    public ISet<string> Skip { get; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Properties that change from call to call, so reading back what was written ignores them.</summary>
    public ISet<string> Volatile { get; } = new HashSet<string>(StringComparer.Ordinal) { "traceId" };
}

/// <summary>Sends the suite's requests and checks every response against the spec.</summary>
internal sealed class RegressionRunner(RegressionSuite suite, HttpClient client, RegressionOptions options)
{
    private static readonly int[] Rejections = [400, 415, 422];

    public async Task<RegressionReport> RunAsync(CancellationToken cancellationToken)
    {
        var cases = new List<CaseResult>();
        foreach (var @case in suite.Cases.Where(options.Include))
        {
            cases.Add(await RunCaseAsync(@case, cancellationToken).ConfigureAwait(false));
        }

        var workflows = new List<WorkflowResult>();
        if (options.Workflows)
        {
            foreach (var workflow in suite.Workflows)
            {
                workflows.Add(await RunWorkflowAsync(workflow, cancellationToken).ConfigureAwait(false));
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
        var exchange = await SendAsync(@case.Request, cancellationToken).ConfigureAwait(false);
        var findings = new List<Finding>();
        var valid = @case.Kind != CaseKind.Invalid;
        Response(@case.Operation, exchange, valid, @case.Name, findings);
        if (valid && Rejections.Contains(exchange.Status))
        {
            Add(findings, Checks.ValidAccepted, @case.Name, exchange, $"a valid request was rejected with {exchange.Status}: {RegressionReport.Excerpt(exchange.Body)}");
        }

        if (!valid)
        {
            Rejected(@case, exchange, findings);
        }

        return new CaseResult(@case, exchange, findings);
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
            var target = Normalize(location[(location.IndexOf('.') is var dot and >= 0 ? dot + 1 : location.Length)..]);
            var keys = errors.Select(e => Normalize(e.Key)).ToList();
            var named = location == "body" || keys.Any(k => k == target || k.StartsWith(target + ".", StringComparison.Ordinal)
                || k.StartsWith(target + "[", StringComparison.Ordinal) || target.StartsWith(k + ".", StringComparison.Ordinal) && k.Length > 0);
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

        if (response.Content[match] is { } schema && OpenApiSpec.IsJson(match))
        {
            var json = exchange.Json();
            if (json is null && exchange.Body.Length > 0)
            {
                Add(findings, Checks.BodyConforms, subject, exchange, $"the body isn't JSON: {RegressionReport.Excerpt(exchange.Body)}");
                return;
            }

            foreach (var error in suite.Validator.Validate(json, schema).Take(10))
            {
                Add(findings, Checks.BodyConforms, subject, exchange, $"the {exchange.Status} body breaks its schema: {error}");
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
        if (workflow.Start.Operation.Body is { } startBody && startRequest.Body is { } sent && JsonNode.Parse(sent) is { } json)
        {
            startRequest = startRequest with { Body = suite.Samples.Unique(json, startBody.Schema, 20_000 + suite.Workflows.ToList().IndexOf(workflow))?.ToJsonString() };
        }

        var start = await SendAsync(startRequest, cancellationToken).ConfigureAwait(false);
        exchanges.Add(start);
        Response(workflow.Start.Operation, start, requestValid: true, workflow.Name, findings);
        if (start.Status.ToString(CultureInfo.InvariantCulture) != workflow.Status)
        {
            return new WorkflowResult(workflow, exchanges, findings, $"{workflow.Start.Name} answered {start.Status}, not {workflow.Status}, so its links weren't followed");
        }

        var links = workflow.Links.Select(l => (Link: l, Operation: suite.Spec.Operations.FirstOrDefault(o => o.Id == l.OperationId))).ToList();
        foreach (var missing in links.Where(l => l.Operation is null))
        {
            findings.Add(new Finding(Checks.ResourceAvailable, Severity.Failure, workflow.Name, $"the link {missing.Link.Name} names '{missing.Link.OperationId}', which the spec doesn't have", string.Empty));
        }

        var reads = links.Where(l => l.Operation?.Method == "GET").ToList();
        var writes = links.Where(l => l.Operation?.Method is "PUT" or "PATCH" or "POST").ToList();
        var deletes = links.Where(l => l.Operation?.Method == "DELETE").ToList();
        var current = start;

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
                else if (SameSchema(current, exchange) && !JsonNode.DeepEquals(Volatile(current.Json()), Volatile(exchange.Json())))
                {
                    Add(findings, Checks.ReadAfterWrite, $"{workflow.Name}: {read.Link.Name} after {after}", exchange,
                        $"reading it back differs at {RegressionReport.FirstDifference(Volatile(current.Json()), Volatile(exchange.Json()), string.Empty)}: "
                        + $"wrote {RegressionReport.Excerpt(current.Body)}, read {RegressionReport.Excerpt(exchange.Body)}");
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

            if (SameSchema(start, exchange))
            {
                current = exchange;
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

        return new WorkflowResult(workflow, exchanges, findings, null);
    }

    private JsonNode? Volatile(JsonNode? node) => RegressionReport.Strip(node, options.Volatile);

    /// <summary>Both responses have the same documented schema ($ref), so one is a representation of what the other is.</summary>
    private bool SameSchema(Exchange a, Exchange b)
    {
        string? Schema(Exchange e) => Operation(e.Request)?.Response(e.Status)?.Content.Values.Select(OpenApiSpec.RefName).FirstOrDefault(n => n is not null);
        return Schema(a) is { } x && x == Schema(b);
    }

    private SpecOperation? Operation(RegressionRequest request)
        => suite.Spec.Operations.FirstOrDefault(o => o.Method == request.Method
            && System.Text.RegularExpressions.Regex.IsMatch(request.Path, "^" + System.Text.RegularExpressions.Regex.Replace(
                System.Text.RegularExpressions.Regex.Escape(o.Path), @"\\\{[^}]+}", "[^/]+") + "$"));

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
    private static string? Evaluate(string expression, Exchange source)
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
            return Text(Pointer(source.Request.Body is { } b ? JsonNode.Parse(b) : null, expression["$request.body".Length..].TrimStart('#')));
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
        JsonValueKind.Number => value.GetValue<decimal>().ToString(CultureInfo.InvariantCulture),
        _ => value.ToJsonString(),
    };

    // ---------------------------------------------------------------- HTTP

    private async Task<Exchange> SendAsync(RegressionRequest request, CancellationToken cancellationToken)
    {
        using var message = request.ToMessage();
        using var response = await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var headers = response.Headers.Concat(response.Content.Headers)
            .GroupBy(h => h.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => string.Join(", ", g.SelectMany(h => h.Value)), StringComparer.OrdinalIgnoreCase);
        return new Exchange(request, (int)response.StatusCode, response.Content.Headers.ContentType?.ToString(), body, headers);
    }

    private void Add(List<Finding> findings, string check, string subject, Exchange exchange, string message, Severity severity = Severity.Failure)
    {
        if (!options.Skip.Contains(check))
        {
            findings.Add(new Finding(check, severity, subject, message, exchange.Request.Curl(client.BaseAddress)));
        }
    }
}
