using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Matchmark.Rules;

namespace Matchmark.Testing;

/// <summary>
/// A regression suite generated from a spec. For every operation:
/// <list type="bullet">
/// <item>the spec's examples, a valid request with only what is required, and one with everything;</item>
/// <item>valid requests with one value at each bound (minLength, maxLength, minimum, maximum, minItems...);</item>
/// <item>one invalid request per constraint: each required value missing, each value null, of the wrong type, out of
/// range, too long or short, off its pattern, off its enum, an unknown or missing discriminator, a body that isn't JSON,
/// or is missing, or is sent as the wrong content type;</item>
/// <item>a workflow for each response with links: follow them, read back what was written, delete it, and check it's gone.</item>
/// </list>
/// Run it against any HTTP API with <see cref="RunAsync"/>; every response is checked against the spec.
/// </summary>
public sealed class RegressionSuite
{
    private RegressionSuite(OpenApiSpec spec, IEnumerable<ArazzoDocument> arazzo)
    {
        Spec = spec;
        Arazzo = arazzo.SelectMany(d => d.Workflows).ToList();
        Validator = new SchemaValidator(spec);
        Samples = new Samples(spec, Validator);
        var generator = new Generator(this);
        Cases = generator.Cases;
        Workflows = generator.Workflows;
        Gaps = generator.Gaps.Concat(Arazzo.SelectMany(w => ArazzoGaps(w).Select(g => $"{w.Name}: {g}"))).ToList();
    }

    /// <summary>The Arazzo workflows given with the spec, run after the cases and the workflows from links.</summary>
    public IReadOnlyList<ArazzoWorkflow> Arazzo { get; }

    public OpenApiSpec Spec { get; }

    public SchemaValidator Validator { get; }

    public Samples Samples { get; }

    public IReadOnlyList<RegressionCase> Cases { get; }

    public IReadOnlyList<RegressionWorkflow> Workflows { get; }

    /// <summary>What the suite couldn't generate, and why (a required value no sample satisfies, say).</summary>
    public IReadOnlyList<string> Gaps { get; }

    /// <summary>The suite for an OpenAPI 3.x document, as JSON, with any Arazzo documents (as JSON) that describe its workflows.</summary>
    public static RegressionSuite FromJson(string specJson, params string[] arazzoJson)
        => new(OpenApiSpec.Parse(specJson), arazzoJson.Select(ArazzoDocument.Parse));

    /// <summary>
    /// The suite for the spec's webhooks, to run against a receiver: each webhook is an operation at <c>/{name}</c> of the
    /// receiver's base address (where the generated <c>Map{Api}Webhooks</c> puts it). A receiver must accept every valid
    /// event, answer as documented, and refuse every invalid one. Null when the spec has no webhooks.
    /// </summary>
    public static RegressionSuite? ForWebhooks(string specJson)
    {
        var spec = JsonNode.Parse(specJson) as JsonObject ?? throw new ArgumentException("The spec isn't a JSON object.", nameof(specJson));
        if (spec["webhooks"] is not JsonObject { Count: > 0 } webhooks)
        {
            return null;
        }

        var paths = new JsonObject();
        foreach (var (name, item) in webhooks)
        {
            paths["/" + name] = item?.DeepClone();
        }

        var receiver = new JsonObject
        {
            ["openapi"] = spec["openapi"]?.DeepClone(),
            ["info"] = spec["info"]?.DeepClone(),
            ["paths"] = paths,
            ["components"] = spec["components"]?.DeepClone(),
        };
        return FromJson(receiver.ToJsonString());
    }

    /// <summary>The operation an Arazzo step calls: its operationId, with or without its source description's name in front.</summary>
    internal SpecOperation? ArazzoOperation(JsonObject step, ArazzoDocument document)
    {
        // operationPath: a JSON Pointer into the source description, '{$sourceDescriptions.x.url}#/paths/~1things~1{id}/get'.
        if (step["operationPath"]?.GetValue<string>() is { } operationPath)
        {
            var match = System.Text.RegularExpressions.Regex.Match(operationPath, "#/paths/([^/]+)/([a-z]+)$");
            if (!match.Success)
            {
                return null;
            }

            var path = match.Groups[1].Value.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            var method = match.Groups[2].Value.ToUpperInvariant();
            return Spec.Operations.FirstOrDefault(o => o.Path == path && o.Method == method);
        }

        var id = step["operationId"]?.GetValue<string>();
        foreach (var source in document.OpenApiSources)
        {
            foreach (var prefix in new[] { $"$sourceDescriptions.{source}.", $"{source}." })
            {
                if (id?.StartsWith(prefix, StringComparison.Ordinal) == true)
                {
                    id = id[prefix.Length..];
                }
            }
        }

        return id is null ? null : Spec.Operations.FirstOrDefault(o => o.Id == id);
    }

    /// <summary>
    /// The workflow a step or a goto names: by its id in the same document, or as
    /// <c>$sourceDescriptions.name.workflows.id</c> in another document given with the spec (the one that has that id).
    /// </summary>
    internal ArazzoWorkflow? NestedWorkflow(string reference, ArazzoDocument document)
    {
        const string marker = ".workflows.";
        if (!reference.StartsWith("$sourceDescriptions.", StringComparison.Ordinal))
        {
            return document.Workflows.FirstOrDefault(w => w.Id == reference);
        }

        var id = reference.Contains(marker, StringComparison.Ordinal) ? reference[(reference.LastIndexOf(marker, StringComparison.Ordinal) + marker.Length)..] : reference;
        var matches = Arazzo.Where(w => w.Id == id).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>What in a workflow the engine can't run: it's reported, and the workflow isn't run rather than run wrongly.</summary>
    public IReadOnlyList<string> ArazzoGaps(ArazzoWorkflow workflow)
    {
        var gaps = new List<string>();
        foreach (var step in workflow.Steps)
        {
            var id = step["stepId"]?.GetValue<string>();
            if (step["workflowId"]?.GetValue<string>() is { } nested)
            {
                if (NestedWorkflow(nested, workflow.Document) is null)
                {
                    gaps.Add($"step {id} runs the workflow {nested}, which isn't in any Arazzo document given");
                }
            }
            else if (step["operationPath"] is { } operationPath && ArazzoOperation(step, workflow.Document) is null)
            {
                gaps.Add($"step {id} names {operationPath}, which isn't an operation in the spec");
            }
            else if (ArazzoOperation(step, workflow.Document) is null)
            {
                gaps.Add($"step {id} calls {step["operationId"]}, which the spec doesn't have");
            }

            var criteria = new[] { step["successCriteria"] }
                .Concat((step["onSuccess"] as JsonArray ?? []).Concat(step["onFailure"] as JsonArray ?? []).Select(a => a?["criteria"]))
                .SelectMany(c => (c as JsonArray ?? []).OfType<JsonObject>());
            foreach (var criterion in criteria.Where(c => !Criteria.Supported(c)))
            {
                gaps.Add($"step {id} has a {Criteria.Type(criterion)} criterion, which isn't supported");
            }
        }

        return gaps;
    }

    public RegressionCase Case(string name)
        => Cases.FirstOrDefault(c => c.Name == name) ?? throw new KeyNotFoundException($"The suite has no case '{name}'.");

    /// <summary>Runs every case and workflow against the API <paramref name="client"/> points at.</summary>
    public Task<RegressionReport> RunAsync(HttpClient client, RegressionOptions? options = null, CancellationToken cancellationToken = default)
        => new RegressionRunner(this, client, options ?? new RegressionOptions()).RunAsync(cancellationToken);

    /// <summary>Runs one case: for a test framework that shows each case as its own test.</summary>
    public Task<CaseResult> RunAsync(HttpClient client, RegressionCase @case, CancellationToken cancellationToken = default)
        => new RegressionRunner(this, client, new RegressionOptions()).RunCaseAsync(@case, cancellationToken);

    /// <summary>Runs one workflow.</summary>
    public Task<WorkflowResult> RunAsync(HttpClient client, RegressionWorkflow workflow, CancellationToken cancellationToken = default)
        => new RegressionRunner(this, client, new RegressionOptions()).RunWorkflowAsync(workflow, cancellationToken);

    public override string ToString()
        => $"{Cases.Count} cases ({string.Join(", ", Cases.GroupBy(c => c.Kind).Select(g => $"{g.Count()} {g.Key.ToString().ToLowerInvariant()}"))}), {Workflows.Count} workflows from links and {Arazzo.Count} from Arazzo, over {Spec.Operations.Count} operations";

    private sealed class Generator
    {
        private readonly RegressionSuite _suite;
        private readonly HashSet<string> _names = new(StringComparer.Ordinal);
        private int _unique;

        private readonly HashSet<string> _rulesBroken = new(StringComparer.Ordinal);
        private int _keys;

        public Generator(RegressionSuite suite)
        {
            _suite = suite;
            foreach (var operation in suite.Spec.Operations)
            {
                Generate(operation);
            }
        }

        public List<RegressionCase> Cases { get; } = [];

        public List<RegressionWorkflow> Workflows { get; } = [];

        public List<string> Gaps { get; } = [];

        private void Generate(SpecOperation operation)
        {
            // The values everything else varies: each parameter's example or sample, and the body's.
            var values = new Dictionary<SpecParameter, JsonNode?>();
            foreach (var parameter in operation.Parameters)
            {
                if (Value(parameter.Example, parameter.Schema, full: false) is { } value)
                {
                    values[parameter] = value.Node;
                }
                else if (parameter.Required)
                {
                    Gaps.Add($"{operation}: no valid value for the {parameter.In} parameter '{parameter.Name}', so it has no cases");
                    return;
                }
            }

            JsonNode? minimal = null, full = null, example = null;
            IReadOnlyList<(string Label, JsonNode? Value)> alternatives = [];
            if (operation.Body is { } body)
            {
                alternatives = _suite.Samples.Alternatives(body.Schema);
                if (Value(null, body.Schema, full: false) is not { } built || alternatives.Count == 0)
                {
                    Gaps.Add($"{operation}: no valid request body could be built from its schema, so it has no cases; give the schema an example");
                    return;
                }

                (minimal, full) = (built.Node, alternatives[0].Value);
                example = body.Example is { } e && _suite.Validator.IsValid(e, body.Schema, Direction.Request) ? e : null;
                if (body.Example is not null && example is null)
                {
                    Gaps.Add($"{operation}: the request body's example doesn't match its schema: {string.Join("; ", _suite.Validator.Validate(body.Example, body.Schema, direction: Direction.Request))}");
                }
            }

            var required = values.Where(v => v.Key.Required).ToDictionary(v => v.Key, v => v.Value);

            // A cursor is opaque: only the API can mint a valid one, so valid requests leave it out (invalid ones still break it).
            var valid = values.Where(v => !Opaque(v.Key)).ToDictionary(v => v.Key, v => v.Value);

            // Valid: the examples, only what is required, everything.
            if (example is not null || operation.Parameters.Any(p => p.Example is not null))
            {
                Add(operation, CaseKind.Example, "the spec's examples", values.Where(v => v.Key.Required || v.Key.Example is not null).ToDictionary(), example ?? minimal);
            }

            Add(operation, CaseKind.Valid, "valid, required values only", required, minimal);
            if (valid.Count > required.Count || full?.ToJsonString() != minimal?.ToJsonString())
            {
                Add(operation, CaseKind.Valid, "valid, every value", valid, full);
            }

            // The same request again, byte for byte: whatever it means (a conflict, the same answer), it must be documented.
            var again = Cases[^1];
            Cases.Add(again with { Name = Unique($"{operation.Id}: valid, the same request again") });

            // Read-only values sent anyway: the server sets them, so it must ignore them or refuse the request.
            if (operation.Body is { } readOnlyBody && full is JsonObject fullObject
                && ReadOnlySentinels(readOnlyBody.Schema) is { Count: > 0 } sentinels)
            {
                var sent = (JsonObject)fullObject.DeepClone();
                foreach (var (name, value) in sentinels)
                {
                    sent[name] = value.DeepClone();
                }

                if (Add(operation, CaseKind.ReadOnlySent, $"valid, with read-only {string.Join(", ", sentinels.Keys)} sent", required, sent, "body")
                    && Cases[^1].Kind == CaseKind.ReadOnlySent)
                {
                    Cases[^1] = Cases[^1] with { ReadOnlySent = sentinels };
                }
            }

            // Every other member of every union in the body.
            foreach (var (label, value) in alternatives.Skip(1))
            {
                Add(operation, CaseKind.Valid, $"valid, every value{label}", values, value);
            }

            // A resource that doesn't exist: every path parameter at a value nothing is likely to have, which reaches the
            // operation's 404 (or an upsert's create).
            var unused = operation.Parameters.Where(p => p.In == "path").ToDictionary(p => p, p => Unused(p.Schema, _suite.Spec.Operations.ToList().IndexOf(operation)));
            if (unused.Count > 0 && unused.Values.All(v => v is not null))
            {
                Add(operation, CaseKind.Valid, "valid, for a resource that doesn't exist",
                    required.ToDictionary(v => v.Key, v => unused.TryGetValue(v.Key, out var u) ? u : v.Value), minimal);
            }

            // Boundaries: each value at each bound it has.
            foreach (var parameter in operation.Parameters.Where(p => values.ContainsKey(p) && !Opaque(p)))
            {
                foreach (var variant in _suite.Samples.Boundaries(values[parameter], parameter.Schema).Where(v => v.Value is not null))
                {
                    Add(operation, CaseKind.Boundary, $"{Where(parameter)} {variant.Description}", With(values, parameter, variant.Value), full, Where(parameter));
                }
            }

            if (operation.Body is { } schemaBody)
            {
                foreach (var variant in alternatives.SelectMany(a => _suite.Samples.Boundaries(a.Value, schemaBody.Schema)))
                {
                    AddOnce(operation, CaseKind.Boundary, $"{BodyWhere(variant.Path)} {variant.Description}", required, variant.Value, BodyWhere(variant.Path));
                }
            }

            // Invalid: each required parameter missing, each value broken, and the body broken every way it can be.
            foreach (var parameter in operation.Parameters.Where(p => p.Required && p.In != "path"))
            {
                Add(operation, CaseKind.Invalid, $"{Where(parameter)} missing", required.Where(v => v.Key != parameter).ToDictionary(), minimal, Where(parameter));
            }

            foreach (var parameter in operation.Parameters.Where(values.ContainsKey))
            {
                foreach (var variant in _suite.Samples.Violations(values[parameter], parameter.Schema).Where(v => Serializable(v.Value) && !ValidAsText(parameter, v.Value)))
                {
                    Add(operation, CaseKind.Invalid, $"{Where(parameter)} {variant.Description}", With(values, parameter, variant.Value), full, Where(parameter));
                }
            }

            if (operation.Body is { } invalidBody)
            {
                foreach (var variant in alternatives.SelectMany(a => _suite.Samples.Violations(a.Value, invalidBody.Schema)))
                {
                    AddOnce(operation, CaseKind.Invalid, $"{BodyWhere(variant.Path)} {variant.Description}", required, variant.Value, BodyWhere(variant.Path));
                }

                Add(operation, CaseKind.Invalid, "body not JSON", required, null, "body", raw: "{\"", contentType: invalidBody.MediaType);
                Add(operation, CaseKind.Invalid, $"body sent as text/plain, not {invalidBody.MediaType}", required, minimal, "body", contentType: "text/plain");
                if (invalidBody.Required)
                {
                    Add(operation, CaseKind.Invalid, "body missing", required, null, "body", raw: string.Empty, contentType: invalidBody.MediaType);
                }
            }

            // Dependencies between parameters (IDL): one request breaking each, with values that are otherwise valid.
            foreach (var dependency in operation.Dependencies)
            {
                var texts = required.Where(r => r.Value is JsonValue).ToDictionary(r => r.Key.Name, r => Text(r.Value), StringComparer.Ordinal);
                if (dependency.Break(texts, name => Sample(operation, name)) is { } broken)
                {
                    var location = string.Join("|", dependency.Parameters.Select(n => operation.Parameters.FirstOrDefault(p => p.Name == n)).Where(p => p is not null).Select(p => Where(p!)));
                    Add(operation, CaseKind.Invalid, $"breaks the dependency {dependency}", Parameters(operation, broken), minimal, location, raw: null, contentType: null, keep: true);
                }
                else
                {
                    Gaps.Add($"{operation}: no valid values break the dependency {dependency}, so it has no case");
                }
            }

            foreach (var unparsed in (operation.Json["x-dependencies"] as JsonArray ?? []).Select(d => d?.GetValue<string>()).Where(d => d is not null && operation.Dependencies.All(p => p.Source != d!.Trim())))
            {
                Gaps.Add($"{operation}: the dependency '{unparsed}' isn't IDL this engine reads");
            }

            // Workflows: from each response with links, starting with a valid request.
            foreach (var (status, response) in operation.Responses.Where(r => r.Value.Links.Count > 0))
            {
                var start = Cases.Where(c => c.Operation == operation && c.Kind is CaseKind.Valid or CaseKind.Example)
                    .OrderBy(c => c.Kind == CaseKind.Example ? 0 : c.Name.EndsWith("valid, required values only", StringComparison.Ordinal) ? 1 : 2)
                    .First();
                Workflows.Add(new RegressionWorkflow($"{operation.Id} {status}, then {string.Join(", ", response.Links.Select(l => l.OperationId))}", start, status, response.Links));
            }
        }

        /// <summary>
        /// A value for each top-level <c>readOnly</c> property, distinctive enough that seeing it in the response means the
        /// server stored what it should have set itself.
        /// </summary>
        private Dictionary<string, JsonNode> ReadOnlySentinels(JsonNode schemaNode)
        {
            var sentinels = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
            foreach (var (name, property) in _suite.Spec.Resolve(schemaNode)["properties"]?.AsObject() ?? [])
            {
                if (property is null || !_suite.Validator.Flag(property, "readOnly"))
                {
                    continue;
                }

                var resolved = _suite.Spec.Resolve(property);
                var types = SchemaValidator.Types(resolved);
                JsonNode? value = resolved["format"]?.GetValue<string>() switch
                {
                    "uuid" => JsonValue.Create("00000000-0000-4000-8000-00000000beef"),
                    "date-time" => JsonValue.Create("1999-12-31T23:59:59Z"),
                    _ when types.Contains("integer") || types.Contains("number") => JsonValue.Create(987654321m),
                    _ when types.Contains("string") => JsonValue.Create("read-only-sent"),
                    _ => null,
                };
                if (value is not null)
                {
                    sentinels[name] = value;
                }
            }

            return sentinels;
        }

        /// <summary>A valid identifier no resource is likely to have: the largest integer allowed, an all-f uuid, a long name.</summary>
        /// <remarks>Each operation gets its own, so one that creates (an upsert) doesn't make another's exist.</remarks>
        private JsonNode? Unused(JsonNode schemaNode, int n)
        {
            var schema = _suite.Spec.Resolve(schemaNode);
            var types = SchemaValidator.Types(schema);
            JsonNode? candidate = types.Contains("integer")
                ? JsonValue.Create((SchemaValidator.Number(schema, "maximum") ?? (schema["format"]?.GetValue<string>() == "int32" ? int.MaxValue : long.MaxValue)) - n)
                : schema["format"]?.GetValue<string>() == "uuid" ? JsonValue.Create($"ffffffff-ffff-4fff-bfff-{n:x12}")
                : types.Contains("string") ? JsonValue.Create($"does-not-exist-{n}")
                : null;
            return candidate is not null && _suite.Validator.IsValid(candidate, schema) ? candidate : null;
        }

        private (JsonNode? Node, bool _)? Value(JsonNode? example, JsonNode schema, bool full)
        {
            if (example is not null && _suite.Validator.IsValid(example, schema, Direction.Request))
            {
                return (example.DeepClone(), true);
            }

            return _suite.Samples.TrySample(schema, full, out var value) ? (value, true) : null;
        }

        private string? Sample(SpecOperation operation, string name)
            => operation.Parameters.FirstOrDefault(p => p.Name == name) is { } parameter && Value(parameter.Example, parameter.Schema, full: false) is { } value
                ? Text(value.Node)
                : null;

        private static Dictionary<SpecParameter, JsonNode?> Parameters(SpecOperation operation, Dictionary<string, string> texts)
            => operation.Parameters.Where(p => texts.ContainsKey(p.Name)).ToDictionary(p => p, p => (JsonNode?)JsonValue.Create(texts[p.Name]));

        /// <summary>Adds the case, unless it can't keep to the operation's dependencies or rules (then false).</summary>
        private bool Add(SpecOperation operation, CaseKind kind, string description, IReadOnlyDictionary<SpecParameter, JsonNode?> parameters, JsonNode? body,
            string? location = null, string? raw = null, string? contentType = null, bool keep = false)
        {
            // Every other case keeps to the operation's dependencies: a valid one is repaired (or dropped), an invalid one
            // about something else is repaired too, so that what it breaks is the one thing that's wrong.
            foreach (var dependency in keep ? [] : operation.Dependencies)
            {
                var texts = parameters.Where(p => p.Value is JsonValue).ToDictionary(p => p.Key.Name, p => Text(p.Value), StringComparer.Ordinal);
                if (dependency.Holds(texts))
                {
                    continue;
                }

                // Repaired without touching the value the case is about.
                var broken = location is null ? null : dependency.Parameters.FirstOrDefault(n => location.EndsWith($".{n}", StringComparison.Ordinal));
                var satisfied = dependency.Satisfy(texts, name => Sample(operation, name));
                if (satisfied is null || broken is not null && satisfied.GetValueOrDefault(broken) != texts.GetValueOrDefault(broken))
                {
                    // Keep the value the case is about, and leave out one of the dependency's other optional values instead.
                    satisfied = broken is null
                        ? null
                        : dependency.Parameters
                            .Where(n => n != broken && texts.ContainsKey(n) && operation.Parameters.FirstOrDefault(p => p.Name == n) is { Required: false })
                            .Select(n => texts.Where(t => t.Key != n).ToDictionary(t => t.Key, t => t.Value, StringComparer.Ordinal))
                            .FirstOrDefault(dependency.Holds);
                }

                if (satisfied is null)
                {
                    if (kind != CaseKind.Invalid)
                    {
                        return false;
                    }

                    continue;
                }

                parameters = Parameters(operation, satisfied);
            }

            // Unique free text, so repeated creates don't collide; a boundary keeps the value it is testing.
            if (kind != CaseKind.Invalid && operation.Body is { } schema)
            {
                body = _suite.Samples.Unique(body, schema.Schema, ++_unique, kind == CaseKind.Boundary ? location?.Split('.').ElementAtOrDefault(1) : null);
            }

            // A body valid by its schema can still break a rule the spec declares (x-matchmark-rules). The first such body
            // for each rule is that rule's invalid case; the rest are dropped, so every valid case keeps to every rule.
            if (kind != CaseKind.Invalid && raw is null && operation.Body is { } ruled && body is not null
                && SchemaRules.Broken(_suite.Spec, body, ruled.Schema).FirstOrDefault(b => b.Error is null) is { Rule: not null } rule)
            {
                if (kind == CaseKind.Example)
                {
                    Gaps.Add($"{operation}: its example breaks the rule {rule.Rule}");
                }

                if (!_rulesBroken.Add($"{operation.Id} {rule.Path} {rule.Rule}"))
                {
                    return false;
                }

                kind = CaseKind.Invalid;
                description = $"{description}, breaking the rule {rule.Rule}";
                location = BodyWhere(rule.Path.TrimStart('$').TrimStart('.'));
            }
            else if (kind == CaseKind.Invalid && raw is null && contentType is null && location?.StartsWith("body.", StringComparison.Ordinal) == true
                     && operation.Body is { } constrained && body is JsonObject sent
                     && SchemaRules.Broken(_suite.Spec, body, constrained.Schema).Any(b => b.Error is null))
            {
                // An invalid body should be wrong in one way: leave out an optional property (not the one it breaks) so
                // that it keeps to the declared rules, or drop the case, which would otherwise say nothing.
                var target = location["body.".Length..].Split('.', '[')[0];
                var mandatory = (_suite.Spec.Resolve(constrained.Schema)["required"] as JsonArray ?? []).Select(n => n?.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
                body = sent.Select(p => p.Key).Where(k => k != target && !mandatory.Contains(k))
                    .Select(k => { var copy = (JsonObject)sent.DeepClone(); copy.Remove(k); return copy; })
                    .FirstOrDefault(copy => !SchemaRules.Broken(_suite.Spec, copy, constrained.Schema).Any(b => b.Error is null));
                if (body is null)
                {
                    return false;
                }
            }

            var path = operation.Path;
            var pathParameters = new Dictionary<string, string>(StringComparer.Ordinal);
            var query = new List<KeyValuePair<string, string>>();
            var headers = new List<KeyValuePair<string, string>>();
            foreach (var (parameter, value) in parameters)
            {
                // Each case is its own attempt, so it gets its own Idempotency-Key, unless the key is what the case is about.
                var texts = string.Equals(parameter.Name, "Idempotency-Key", StringComparison.OrdinalIgnoreCase) && parameter.In == "header"
                            && location != $"header.{parameter.Name}"
                    ? [$"key-{++_keys}"]
                    : Texts(value);
                foreach (var text in texts)
                {
                    switch (parameter.In)
                    {
                        case "path":
                            path = path.Replace($"{{{parameter.Name}}}", Uri.EscapeDataString(text), StringComparison.Ordinal);
                            pathParameters[parameter.Name] = text;
                            break;
                        case "query":
                            query.Add(new(parameter.Name, text));
                            break;
                        case "header":
                            headers.Add(new(parameter.Name, text));
                            break;
                    }
                }
            }

            var hasBody = operation.Body is not null && (raw is not null || body is not null || kind == CaseKind.Invalid && location is not null && location.StartsWith("body", StringComparison.Ordinal));
            var request = new RegressionRequest(
                operation.Method,
                path,
                query,
                headers,
                hasBody ? raw ?? (body ?? JsonValue.Create<string?>(null))?.ToJsonString() ?? "null" : null,
                hasBody ? contentType ?? operation.Body!.MediaType : null)
            {
                PathParameters = pathParameters,
            };

            Cases.Add(new RegressionCase(Unique($"{operation.Id}: {description}"), operation, kind, request, location));
            return true;
        }

        /// <summary>A case from another union member's body, unless the first member's already made one of that name.</summary>
        private void AddOnce(SpecOperation operation, CaseKind kind, string description, IReadOnlyDictionary<SpecParameter, JsonNode?> parameters, JsonNode? body,
            string? location = null)
        {
            if (!_names.Contains($"{operation.Id}: {description}"))
            {
                Add(operation, kind, description, parameters, body, location);
            }
        }

        private string Unique(string name)
        {
            var unique = name;
            for (var n = 2; !_names.Add(unique); n++)
            {
                unique = $"{name} ({n})";
            }

            return unique;
        }

        private static Dictionary<SpecParameter, JsonNode?> With(IReadOnlyDictionary<SpecParameter, JsonNode?> values, SpecParameter parameter, JsonNode? value)
            => values.Where(v => v.Key.Required || v.Key == parameter).ToDictionary(v => v.Key, v => v.Key == parameter ? value : v.Value);

        private static string Where(SpecParameter parameter) => $"{parameter.In}.{parameter.Name}";

        private static bool Opaque(SpecParameter parameter) => parameter.In == "query" && RegressionRunner.Cursors.Contains(parameter.Name.ToLowerInvariant());

        private static string BodyWhere(string path) => path.Length == 0 ? "body" : path.StartsWith('[') ? "body" + path : $"body.{path}";

        /// <summary>
        /// A parameter travels as text, so a violation only counts if its text is invalid too: for a string parameter the
        /// number 12345 is sent as "12345", which is a valid string.
        /// </summary>
        private bool ValidAsText(SpecParameter parameter, JsonNode? value)
            => value is JsonValue && SchemaValidator.Types(_suite.Spec.Resolve(parameter.Schema)) is var types && types.Contains("string") && !types.Contains("null")
               && _suite.Validator.IsValid(JsonValue.Create(Text(value)), parameter.Schema);

        /// <summary>A parameter value can only be sent as text: a scalar, or an array of scalars.</summary>
        private static bool Serializable(JsonNode? value)
            => value is JsonValue || value is JsonArray array && array.All(i => i is JsonValue);

        private static IEnumerable<string> Texts(JsonNode? value) => value switch
        {
            null => [],
            JsonArray array => array.Select(Text),
            _ => [Text(value)],
        };

        private static string Text(JsonNode? value) => value?.GetValueKind() switch
        {
            JsonValueKind.String => value.GetValue<string>(),
            JsonValueKind.Number => SchemaValidator.NumberOf(value)?.ToString(CultureInfo.InvariantCulture) ?? value.ToJsonString(),
            null => string.Empty,
            _ => value.ToJsonString(),
        };
    }
}
