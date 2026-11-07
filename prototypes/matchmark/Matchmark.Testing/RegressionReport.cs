using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Matchmark.Testing;

/// <summary>The checks the runner makes. Each one names its findings.</summary>
public static class Checks
{
    /// <summary>No 5xx, unless the spec documents that exact status.</summary>
    public const string NotAServerError = nameof(NotAServerError);

    /// <summary>The status is documented for the operation. For an invalid request, an undocumented 4xx is only a warning.</summary>
    public const string StatusDocumented = nameof(StatusDocumented);

    /// <summary>The content type is one the spec documents for that status, and a response documented without a body has none.</summary>
    public const string ContentTypeDocumented = nameof(ContentTypeDocumented);

    /// <summary>The body matches the documented schema.</summary>
    public const string BodyConforms = nameof(BodyConforms);

    /// <summary>The response has every header the spec marks required.</summary>
    public const string HeadersDocumented = nameof(HeadersDocumented);

    /// <summary>A request that passes every schema isn't rejected (400, 415, 422).</summary>
    public const string ValidAccepted = nameof(ValidAccepted);

    /// <summary>A request that breaks a constraint is rejected with a 4xx.</summary>
    public const string InvalidRejected = nameof(InvalidRejected);

    /// <summary>A validation problem (a body with "errors") names the value that was wrong.</summary>
    public const string RejectionNamesTheValue = nameof(RejectionNamesTheValue);

    /// <summary>Following a link from a successful response succeeds.</summary>
    public const string ResourceAvailable = nameof(ResourceAvailable);

    /// <summary>Reading back what was just written gives what the write returned.</summary>
    public const string ReadAfterWrite = nameof(ReadAfterWrite);

    /// <summary>After a successful delete, reading the resource gives 404 or 410.</summary>
    public const string DeletedIsGone = nameof(DeletedIsGone);

    /// <summary>A safe request (GET, HEAD, QUERY) changes nothing: what the API shows is the same before and after (RFC 9110 §9.2.1).</summary>
    public const string SafeChangesNothing = nameof(SafeChangesNothing);

    /// <summary>An idempotent request (PUT, DELETE) twice has the same effect as once (RFC 9110 §9.2.2).</summary>
    public const string IdempotentTwice = nameof(IdempotentTwice);

    /// <summary>A success's Location header can be read, when the spec has an operation that reads it.</summary>
    public const string LocationReadable = nameof(LocationReadable);

    /// <summary>A read-only value sent in a request is ignored or refused, never stored.</summary>
    public const string ReadOnlyIgnored = nameof(ReadOnlyIgnored);

    /// <summary>A write-only value (a password, a secret) never appears in a response.</summary>
    public const string WriteOnlyHidden = nameof(WriteOnlyHidden);

    /// <summary>Sending the same Idempotency-Key and body again gives the same answer, and creates nothing new.</summary>
    public const string IdempotentReplay = nameof(IdempotentReplay);

    /// <summary>An Idempotency-Key reused with a different body is refused (the draft says 422), never applied.</summary>
    public const string IdempotencyKeyReuse = nameof(IdempotencyKeyReuse);

    /// <summary>A successful conditional change gives a new ETag.</summary>
    public const string ETagChanges = nameof(ETagChanges);

    /// <summary>A change sent with a stale If-Match is refused with 412, so no concurrent change is lost.</summary>
    public const string StaleIfMatchRefused = nameof(StaleIfMatchRefused);

    /// <summary>Walking a paged list's cursors repeats nothing, ends, and finds what one big page holds.</summary>
    public const string PagesComplete = nameof(PagesComplete);

    /// <summary>Work accepted with 202 reaches a final state when polled.</summary>
    public const string OperationCompletes = nameof(OperationCompletes);

    /// <summary>A 429 says when to try again (Retry-After).</summary>
    public const string RetryAfterGiven = nameof(RetryAfterGiven);

    /// <summary>A deprecated operation announces it with a Deprecation header (RFC 9745), and its Sunset isn't earlier.</summary>
    public const string DeprecationAnnounced = nameof(DeprecationAnnounced);

    /// <summary>An operation's x-matchmark-ensures (Arazzo criteria over the request and response) hold on each success.</summary>
    public const string Postcondition = nameof(Postcondition);

    /// <summary>Every x-matchmark-rules (CEL over self) on a schema holds for each instance of it in a response.</summary>
    public const string SchemaRule = nameof(SchemaRule);

    /// <summary>Every x-matchmark-transitions (CEL over self and oldSelf) holds between two readings of a resource.</summary>
    public const string TransitionRule = nameof(TransitionRule);

    /// <summary>A metamorphic relation holds: the first n results are the start of the first n + 1 (prefix).</summary>
    public const string RelationHolds = nameof(RelationHolds);

    /// <summary>Each step of an Arazzo workflow meets its success criteria, or its failure is one the workflow handles.</summary>
    public const string WorkflowCriteria = nameof(WorkflowCriteria);
}

public enum Severity
{
    Failure,
    Warning,
}

/// <summary>Something a check found: which check, about what, and how to reproduce it.</summary>
public sealed record Finding(string Check, Severity Severity, string Subject, string Message, string Reproduce)
{
    public override string ToString() => $"[{Check}] {Subject}: {Message}";
}

/// <summary>A request and the response it got.</summary>
public sealed record Exchange(RegressionRequest Request, int Status, string? ContentType, string Body, IReadOnlyDictionary<string, string> Headers)
{
    public JsonNode? Json()
    {
        try
        {
            return Body.Length == 0 ? null : JsonNode.Parse(Body);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public override string ToString() => $"{Request} → {Status}";
}

public sealed record CaseResult(RegressionCase Case, Exchange Exchange, IReadOnlyList<Finding> Findings)
{
    public bool Passed => Findings.All(f => f.Severity != Severity.Failure);

    public override string ToString()
        => $"{Case.Name}: {Exchange.Status}{string.Concat(Findings.Select(f => "\n  " + f))}";
}

public sealed record WorkflowResult(string Name, IReadOnlyList<Exchange> Exchanges, IReadOnlyList<Finding> Findings, string? Skipped)
{
    public bool Passed => Findings.All(f => f.Severity != Severity.Failure);

    public override string ToString()
        => $"{Name}: {(Skipped ?? string.Join(", ", Exchanges.Select(e => $"{e.Request.Method} {e.Status}")))}{string.Concat(Findings.Select(f => "\n  " + f))}";
}

/// <summary>A documented outcome (an operation's status), and whether any request reached it.</summary>
public sealed record Outcome(string OperationId, string Status, bool Reached);

/// <summary>Everything a run found, and how much of the spec it reached.</summary>
public sealed class RegressionReport(IReadOnlyList<CaseResult> cases, IReadOnlyList<WorkflowResult> workflows, IReadOnlyList<Outcome> outcomes)
{
    public IReadOnlyList<CaseResult> Cases { get; } = cases;

    public IReadOnlyList<WorkflowResult> Workflows { get; } = workflows;

    public IReadOnlyList<Outcome> Outcomes { get; } = outcomes;

    public IEnumerable<Finding> Findings => Cases.SelectMany(c => c.Findings).Concat(Workflows.SelectMany(w => w.Findings));

    public IReadOnlyList<Finding> Failures => Findings.Where(f => f.Severity == Severity.Failure).ToList();

    public IReadOnlyList<Finding> Warnings => Findings.Where(f => f.Severity == Severity.Warning).ToList();

    public bool Passed => Failures.Count == 0;

    /// <summary>
    /// How this run's responses differ from <paramref name="other"/>'s, case by case and workflow step by step: status,
    /// then body (ignoring <paramref name="ignore"/>d properties, such as a trace id). For running one suite against two
    /// implementations that should behave the same.
    /// </summary>
    public IReadOnlyList<string> Compare(RegressionReport other, params string[] ignore)
    {
        var ignored = new HashSet<string>(ignore.Length == 0 ? ["traceId"] : ignore, StringComparer.Ordinal);
        var differences = new List<string>();
        var theirs = other.Cases.ToDictionary(c => c.Case.Name, StringComparer.Ordinal);
        foreach (var mine in Cases)
        {
            if (!theirs.TryGetValue(mine.Case.Name, out var match))
            {
                differences.Add($"{mine.Case.Name}: only in this run");
                continue;
            }

            Difference(mine.Case.Name, mine.Exchange, match.Exchange, ignored, differences);
        }

        var theirFlows = other.Workflows.ToDictionary(w => w.Name, StringComparer.Ordinal);
        foreach (var mine in Workflows)
        {
            if (!theirFlows.TryGetValue(mine.Name, out var match))
            {
                continue;
            }

            for (var i = 0; i < Math.Max(mine.Exchanges.Count, match.Exchanges.Count); i++)
            {
                if (i >= mine.Exchanges.Count || i >= match.Exchanges.Count)
                {
                    differences.Add($"{mine.Name}: {mine.Exchanges.Count} steps here, {match.Exchanges.Count} there");
                    break;
                }

                Difference($"{mine.Name}, step {i + 1} ({mine.Exchanges[i].Request})", mine.Exchanges[i], match.Exchanges[i], ignored, differences);
            }
        }

        return differences;
    }

    private static void Difference(string subject, Exchange mine, Exchange theirs, HashSet<string> ignored, List<string> differences)
    {
        if (mine.Status != theirs.Status)
        {
            differences.Add($"{subject}: {mine.Status} here, {theirs.Status} there");
            return;
        }

        var a = Strip(mine.Json(), ignored);
        var b = Strip(theirs.Json(), ignored);
        if (a is null && b is null ? mine.Body != theirs.Body : !JsonNode.DeepEquals(a, b))
        {
            differences.Add($"{subject}: the bodies differ{(FirstDifference(a, b, string.Empty) is { } at ? $" at {at}" : string.Empty)}: {Excerpt(mine.Body)} here, {Excerpt(theirs.Body)} there");
        }
    }

    public static JsonNode? Strip(JsonNode? node, ISet<string> ignored)
    {
        switch (node)
        {
            case JsonObject obj:
                var copy = new JsonObject();
                foreach (var (name, value) in obj)
                {
                    if (!ignored.Contains(name))
                    {
                        copy[name] = Strip(value, ignored);
                    }
                }

                return copy;
            case JsonArray array:
                return new JsonArray(array.Select(i => Strip(i, ignored)).ToArray());
            default:
                return node?.DeepClone();
        }
    }

    /// <summary>The path of the first place two JSON values differ.</summary>
    public static string? FirstDifference(JsonNode? a, JsonNode? b, string path)
    {
        if (JsonNode.DeepEquals(a, b))
        {
            return null;
        }

        switch (a, b)
        {
            case (JsonObject x, JsonObject y):
                foreach (var name in x.Select(p => p.Key).Union(y.Select(p => p.Key)))
                {
                    if (FirstDifference(x[name], y[name], SchemaValidator.Join(path, name)) is { } found)
                    {
                        return found;
                    }
                }

                return path;
            case (JsonArray x, JsonArray y) when x.Count == y.Count:
                for (var i = 0; i < x.Count; i++)
                {
                    if (FirstDifference(x[i], y[i], $"{path}[{i}]") is { } found)
                    {
                        return found;
                    }
                }

                return path;
            default:
                return path.Length == 0 ? "the body" : path;
        }
    }

    public static string Excerpt(string body) => body.Length == 0 ? "(no body)" : body.Length <= 300 ? body : body[..300] + "…";

    public override string ToString()
    {
        var text = new StringBuilder();
        var reached = Outcomes.Count(o => o.Reached);
        text.AppendLine(CultureInfo.InvariantCulture, $"{Cases.Count} cases, {Workflows.Count} workflows: {Failures.Count} failures, {Warnings.Count} warnings.");
        text.AppendLine(CultureInfo.InvariantCulture, $"Documented outcomes reached: {reached} of {Outcomes.Count}.");
        foreach (var missed in Outcomes.Where(o => !o.Reached))
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"  not reached: {missed.OperationId} {missed.Status}");
        }

        foreach (var workflow in Workflows)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"Workflow {workflow.Name}: {workflow.Skipped ?? string.Join(" → ", workflow.Exchanges.Select(e => $"{e.Request.Method} {e.Status}"))}");
        }

        foreach (var group in Findings.OrderBy(f => f.Severity).GroupBy(f => (f.Severity, f.Check)))
        {
            text.AppendLine();
            text.AppendLine(CultureInfo.InvariantCulture, $"{group.Key.Severity}s of {group.Key.Check} ({group.Count()}):");
            foreach (var finding in group)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"  {finding.Subject}: {finding.Message}");
                text.AppendLine(CultureInfo.InvariantCulture, $"    {finding.Reproduce}");
            }
        }

        return text.ToString();
    }
}
