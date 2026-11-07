using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

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
    private RegressionSuite(OpenApiSpec spec)
    {
        Spec = spec;
        Validator = new SchemaValidator(spec);
        Samples = new Samples(spec, Validator);
        var generator = new Generator(this);
        Cases = generator.Cases;
        Workflows = generator.Workflows;
        Gaps = generator.Gaps;
    }

    public OpenApiSpec Spec { get; }

    public SchemaValidator Validator { get; }

    public Samples Samples { get; }

    public IReadOnlyList<RegressionCase> Cases { get; }

    public IReadOnlyList<RegressionWorkflow> Workflows { get; }

    /// <summary>What the suite couldn't generate, and why (a required value no sample satisfies, say).</summary>
    public IReadOnlyList<string> Gaps { get; }

    /// <summary>The suite for an OpenAPI 3.x document, as JSON.</summary>
    public static RegressionSuite FromJson(string specJson) => new(OpenApiSpec.Parse(specJson));

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
        => $"{Cases.Count} cases ({string.Join(", ", Cases.GroupBy(c => c.Kind).Select(g => $"{g.Count()} {g.Key.ToString().ToLowerInvariant()}"))}) and {Workflows.Count} workflows over {Spec.Operations.Count} operations";

    private sealed class Generator
    {
        private readonly RegressionSuite _suite;
        private readonly HashSet<string> _names = new(StringComparer.Ordinal);
        private int _unique;

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
                example = body.Example is { } e && _suite.Validator.IsValid(e, body.Schema) ? e : null;
                if (body.Example is not null && example is null)
                {
                    Gaps.Add($"{operation}: the request body's example doesn't match its schema: {string.Join("; ", _suite.Validator.Validate(body.Example, body.Schema))}");
                }
            }

            var required = values.Where(v => v.Key.Required).ToDictionary(v => v.Key, v => v.Value);

            // Valid: the examples, only what is required, everything.
            if (example is not null || operation.Parameters.Any(p => p.Example is not null))
            {
                Add(operation, CaseKind.Example, "the spec's examples", values.Where(v => v.Key.Required || v.Key.Example is not null).ToDictionary(), example ?? minimal);
            }

            Add(operation, CaseKind.Valid, "valid, required values only", required, minimal);
            if (values.Count > required.Count || full?.ToJsonString() != minimal?.ToJsonString())
            {
                Add(operation, CaseKind.Valid, "valid, every value", values, full);
            }

            // The same request again, byte for byte: whatever it means (a conflict, the same answer), it must be documented.
            var again = Cases[^1];
            Cases.Add(again with { Name = Unique($"{operation.Id}: valid, the same request again") });

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
            foreach (var parameter in operation.Parameters.Where(values.ContainsKey))
            {
                foreach (var variant in _suite.Samples.Boundaries(values[parameter], parameter.Schema).Where(v => v.Value is not null))
                {
                    Add(operation, CaseKind.Boundary, $"{Where(parameter)} {variant.Description}", With(values, parameter, variant.Value), full);
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
                foreach (var variant in _suite.Samples.Violations(values[parameter], parameter.Schema).Where(v => Serializable(v.Value)))
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

            // Workflows: from each response with links, starting with a valid request.
            foreach (var (status, response) in operation.Responses.Where(r => r.Value.Links.Count > 0))
            {
                var start = Cases.Last(c => c.Operation == operation && c.Kind is CaseKind.Valid or CaseKind.Example);
                Workflows.Add(new RegressionWorkflow($"{operation.Id} {status}, then {string.Join(", ", response.Links.Select(l => l.OperationId))}", start, status, response.Links));
            }
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
            if (example is not null && _suite.Validator.IsValid(example, schema))
            {
                return (example.DeepClone(), true);
            }

            return _suite.Samples.TrySample(schema, full, out var value) ? (value, true) : null;
        }

        private void Add(SpecOperation operation, CaseKind kind, string description, IReadOnlyDictionary<SpecParameter, JsonNode?> parameters, JsonNode? body,
            string? location = null, string? raw = null, string? contentType = null)
        {
            // Unique free text, so repeated creates don't collide; a boundary keeps the value it is testing.
            if (kind != CaseKind.Invalid && operation.Body is { } schema)
            {
                body = _suite.Samples.Unique(body, schema.Schema, ++_unique, kind == CaseKind.Boundary ? location?.Split('.').ElementAtOrDefault(1) : null);
            }

            var path = operation.Path;
            var pathParameters = new Dictionary<string, string>(StringComparer.Ordinal);
            var query = new List<KeyValuePair<string, string>>();
            var headers = new List<KeyValuePair<string, string>>();
            foreach (var (parameter, value) in parameters)
            {
                foreach (var text in Texts(value))
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

        private static string BodyWhere(string path) => path.Length == 0 ? "body" : path.StartsWith('[') ? "body" + path : $"body.{path}";

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
