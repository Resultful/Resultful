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

    /// <summary>A message in a workflow (sent by it, or received from the API) conforms to its AsyncAPI message.</summary>
    public const string MessageConforms = nameof(MessageConforms);

    /// <summary>A write that repeats another resource's unique key (x-matchmark-unique) is refused with the documented status.</summary>
    public const string UniqueEnforced = nameof(UniqueEnforced);

    /// <summary>A reference to nothing (x-matchmark-references) is refused, and so is deleting what is still referred to when the reference restricts it.</summary>
    public const string ReferenceHolds = nameof(ReferenceHolds);

    /// <summary>A derived field (x-matchmark-derived) equals its expression in a create's response.</summary>
    public const string DerivedHolds = nameof(DerivedHolds);

    /// <summary>A list is in its declared order (x-matchmark-order).</summary>
    public const string ListOrdered = nameof(ListOrdered);
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
    /// The fidelity of this run (against a fake or twin) to <paramref name="real"/> (the same suite against the real API,
    /// from the same seed state): the share of steps, case by case and workflow step by step, whose status matches and
    /// whose body matches once volatile values are left out (ids, anything ending in Id or At, ETags, trace ids, and
    /// <paramref name="volatileNames"/>). Per operation and overall (GAPS.md, gap 2).
    /// </summary>
    public FidelityReport Fidelity(RegressionReport real, params string[] volatileNames) => Fidelity(Record(real), volatileNames);

    /// <summary>
    /// The fidelity of this run to a recording of the real API's (<see cref="Record"/>): what PR builds compare against,
    /// so they need neither the real API nor its credentials.
    /// </summary>
    public FidelityReport Fidelity(FidelityRecording real, params string[] volatileNames)
    {
        var ignored = new HashSet<string>(volatileNames, StringComparer.OrdinalIgnoreCase) { "id", "traceId", "etag", "createdAt", "updatedAt" };
        // The real API's steps are what's measured: one the fake never reached (a workflow it stopped early) doesn't match.
        var mine = Record(this).Steps.GroupBy(s => s.Subject, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var steps = real.Steps.GroupBy(s => s.Subject, StringComparer.Ordinal).Select(g => g.First())
            .Select(s => (s.Operation, s.Subject, Mine: mine.GetValueOrDefault(s.Subject)?.Exchange(), Theirs: s.Exchange())).ToList();

        var mismatches = new List<string>();
        var tasks = new List<FidelityTask>();
        var operations = steps.GroupBy(s => s.Operation, StringComparer.Ordinal).Select(group =>
        {
            var matched = 0;
            foreach (var (operation, subject, here, other) in group)
            {
                string? difference = null;
                if (here is null)
                {
                    mismatches.Add($"{subject}: not reached here; the real API answered {other.Status}");
                    difference = "not reached";
                }
                else if (here.Status != other.Status)
                {
                    mismatches.Add($"{subject}: {here.Status} here, {other.Status} from the real API");
                    difference = "status";
                }
                else if (!JsonNode.DeepEquals(Normalized(here, ignored), Normalized(other, ignored)))
                {
                    difference = FirstDifference(Normalized(here, ignored), Normalized(other, ignored), string.Empty);
                    mismatches.Add($"{subject}: the bodies differ at {difference}");
                }

                if (difference is not null)
                {
                    tasks.Add(new FidelityTask(operation, subject, here?.Status, here?.Body, other.Status, other.Body, difference));
                }
                else
                {
                    matched++;
                }
            }

            return new OperationFidelity(group.Key, group.Count(), matched);
        }).OrderBy(o => o.Operation, StringComparer.Ordinal).ToList();
        return new FidelityReport(operations, mismatches) { Tasks = tasks };
    }

    /// <summary>A run's steps (each case, each workflow step) and their answers: a recording to replay a fidelity run against.</summary>
    public static FidelityRecording Record(RegressionReport report)
        => new(Steps(report).Select(s => RecordedStep.Of(s.Operation, s.Subject, s.Exchange)).ToList());

    /// <summary>A run's steps in the order they ran, each named as a recording names it.</summary>
    internal static IEnumerable<(string Operation, string Subject, Exchange Exchange)> Steps(RegressionReport report)
    {
        foreach (var result in report.Cases)
        {
            yield return (result.Case.Operation.Id, result.Case.Name, result.Exchange);
        }

        foreach (var workflow in report.Workflows)
        {
            // Named without the ids in their paths, which differ between the two runs.
            for (var i = 0; i < workflow.Exchanges.Count; i++)
            {
                var e = workflow.Exchanges[i];
                yield return (workflow.Name,
                    $"{workflow.Name}, step {i + 1} ({e.Request.Method} {System.Text.RegularExpressions.Regex.Replace(e.Request.Path, "/(?:[0-9]+|[0-9a-fA-F-]{36})(?=/|$)", "/{}")})", e);
            }
        }
    }

    /// <summary>
    /// What a step's answer says, without the wording: a problem is its status and which values it names (its title,
    /// type and messages are how it's put); anything else is its JSON, volatile values and nulls left out.
    /// </summary>
    private static JsonNode? Normalized(Exchange exchange, HashSet<string> ignored)
    {
        var json = exchange.Json() ?? (exchange.Body.Length == 0 ? null : JsonValue.Create(exchange.Body));
        if (json is JsonObject problem && (exchange.ContentType?.Contains("problem", StringComparison.OrdinalIgnoreCase) == true || problem["errors"] is JsonObject))
        {
            return new JsonObject
            {
                ["status"] = exchange.Status,
                ["names"] = new JsonArray((problem["errors"] as JsonObject ?? []).Select(e => e.Key.TrimStart('$', '.').ToLowerInvariant())
                    .Order(StringComparer.Ordinal).Select(k => (JsonNode?)JsonValue.Create(k)).ToArray()),
            };
        }

        return Volatile(json?.DeepClone(), ignored);
    }

    private static JsonNode? Volatile(JsonNode? node, HashSet<string> ignored)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var name in obj.Select(p => p.Key).ToList())
                {
                    if (obj[name] is null || ignored.Contains(name) || name.Length > 2 && (name.EndsWith("Id", StringComparison.Ordinal) || name.EndsWith("At", StringComparison.Ordinal)))
                    {
                        obj.Remove(name);
                    }
                    else
                    {
                        obj[name] = Volatile(obj[name], ignored);
                    }
                }

                return obj;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    array[i] = Volatile(array[i]?.DeepClone(), ignored);
                }

                return array;
            default:
                return node;
        }
    }

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

/// <summary>How many of an operation's steps matched the real API.</summary>
public sealed record OperationFidelity(string Operation, int Steps, int Matched)
{
    public double Score => Steps == 0 ? 1 : (double)Matched / Steps;

    public override string ToString() => $"{Operation}: {Matched}/{Steps} ({Score * 100:0}%)";
}

/// <summary>A fake's fidelity to the real API: per operation, overall, and each step that differed.</summary>
public sealed record FidelityReport(IReadOnlyList<OperationFidelity> Operations, IReadOnlyList<string> Mismatches)
{
    /// <summary>The mismatches as tasks for whoever writes the twin's behaviour (<see cref="FidelityLoop"/>).</summary>
    public IReadOnlyList<FidelityTask> Tasks { get; init; } = [];

    public int Steps => Operations.Sum(o => o.Steps);

    public int Matched => Operations.Sum(o => o.Matched);

    public double Score => Steps == 0 ? 1 : (double)Matched / Steps;

    public override string ToString()
        => $"Fidelity: {Matched}/{Steps} steps match the real API ({Score * 100:0.#}%).\n"
           + string.Join("\n", Operations.Select(o => $"  {o}"))
           + (Mismatches.Count == 0 ? string.Empty : "\nMismatches:\n" + string.Join("\n", Mismatches.Select(m => $"  {m}")));
}

/// <summary>One step of a recorded run: which operation, which case or workflow step, and the answer.</summary>
public sealed record RecordedStep(string Operation, string Subject, int Status, string? ContentType, string Body)
{
    public static RecordedStep Of(string operation, string subject, Exchange exchange)
        => new(operation, subject, exchange.Status, exchange.ContentType, exchange.Body);

    internal Exchange Exchange()
        => new(new RegressionRequest(string.Empty, string.Empty, [], [], null, null), Status, ContentType, Body, new Dictionary<string, string>());
}

/// <summary>A recording of the real API's answers to the suite, saved as JSON, for fidelity runs that don't call it.</summary>
public sealed record FidelityRecording(IReadOnlyList<RecordedStep> Steps)
{
    private static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web) { WriteIndented = true };

    public string ToJson() => System.Text.Json.JsonSerializer.Serialize(this, Json);

    public static FidelityRecording FromJson(string json)
        => System.Text.Json.JsonSerializer.Deserialize<FidelityRecording>(json, Json) ?? throw new ArgumentException("The recording is empty.", nameof(json));
}
