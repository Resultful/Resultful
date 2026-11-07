using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Matchmark.Testing;

/// <summary>
/// The invariants an overlay adds to a spec: postconditions (<c>x-matchmark-ensures</c>, Arazzo criteria), rules on a
/// schema (<c>x-matchmark-rules</c>, CEL over <c>self</c>), rules between two readings (<c>x-matchmark-transitions</c>,
/// CEL over <c>self</c> and <c>oldSelf</c>) and metamorphic relations (<c>x-matchmark-relations</c>).
/// </summary>
internal sealed partial class RegressionRunner
{
    /// <summary>The invariants a successful response must keep: the operation's postconditions and its schemas' rules.</summary>
    private void Invariants(SpecOperation operation, Exchange exchange, JsonNode? json, JsonNode schema, string subject, List<Finding> findings)
    {
        if (!options.Skip.Contains(Checks.Postcondition))
        {
            var context = new ArazzoContext([]) { Current = exchange };
            foreach (var ensures in (operation.Json["x-matchmark-ensures"] as JsonArray ?? []).OfType<JsonObject>())
            {
                bool holds;
                try
                {
                    holds = Criteria.Holds(ensures, context);
                }
                catch (FormatException e)
                {
                    Add(findings, Checks.Postcondition, subject, exchange, $"the postcondition can't be read: {e.Message}", Severity.Warning);
                    continue;
                }

                if (!holds)
                {
                    Add(findings, Checks.Postcondition, subject, exchange,
                        $"the postcondition {ensures["condition"]} doesn't hold: {RegressionReport.Excerpt(exchange.Body)}");
                }
            }
        }

        if (!options.Skip.Contains(Checks.SchemaRule))
        {
            Rules(json, schema, "$", 0, (path, rule, message, error) => Add(findings, Checks.SchemaRule, subject, exchange,
                error is null
                    ? $"{path} breaks the rule {rule}{(message is null ? string.Empty : $" ({message})")}: {RegressionReport.Excerpt((Find(json, path) ?? json)?.ToJsonString() ?? "null")}"
                    : $"{path}: the rule {rule} can't be evaluated: {error}",
                error is null ? Severity.Failure : Severity.Warning));
        }
    }

    /// <summary>Every x-matchmark-rules on the schemas an instance is an instance of, checked: its properties, items and allOf.</summary>
    private void Rules(JsonNode? value, JsonNode schema, string path, int depth, Action<string, string, string?, string?> broken)
    {
        if (depth > 32)
        {
            return;
        }

        var resolved = suite.Spec.Resolve(schema);
        foreach (var rule in (resolved["x-matchmark-rules"] as JsonArray ?? []).OfType<JsonObject>())
        {
            if (rule["rule"]?.GetValue<string>() is { } text && Cel.Check(text, value) is { Holds: false } result)
            {
                broken(path, text, rule["message"]?.GetValue<string>(), result.Error);
            }
        }

        foreach (var part in (resolved["allOf"] as JsonArray ?? []).Where(p => p is not null))
        {
            Rules(value, part!, path, depth + 1, broken);
        }

        switch (value)
        {
            case JsonObject obj when resolved["properties"] is JsonObject properties:
                foreach (var (name, property) in properties)
                {
                    if (property is not null && obj.TryGetPropertyValue(name, out var child))
                    {
                        Rules(child, property, $"{path}.{name}", depth + 1, broken);
                    }
                }

                break;
            case JsonArray array when resolved["items"] is { } items:
                for (var i = 0; i < array.Count; i++)
                {
                    Rules(array[i], items, $"{path}[{i}]", depth + 1, broken);
                }

                break;
        }
    }

    /// <summary>The value at a path the rules walk made ("$.items[0].sku").</summary>
    private static JsonNode? Find(JsonNode? root, string path)
    {
        var node = root;
        foreach (var part in path.TrimStart('$').Split('.', StringSplitOptions.RemoveEmptyEntries).SelectMany(p => p.Split('[')))
        {
            node = part.EndsWith(']') && int.TryParse(part[..^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
                ? (node as JsonArray)?.ElementAtOrDefault(i)
                : (node as JsonObject)?[part];
        }

        return node;
    }

    /// <summary>Checks the x-matchmark-transitions of a resource's schema between two readings of it.</summary>
    private bool Transitions(Exchange before, Exchange after, string subject, List<Finding> findings)
    {
        if (options.Skip.Contains(Checks.TransitionRule)
            || Operation(after.Request)?.Response(after.Status)?.Content.Values.FirstOrDefault(s => s is not null) is not { } schema)
        {
            return true;
        }

        var resolved = suite.Spec.Resolve(schema);
        var rules = (resolved["x-matchmark-transitions"] as JsonArray ?? []).OfType<JsonObject>()
            .Concat((resolved["allOf"] as JsonArray ?? []).Where(p => p is not null)
                .SelectMany(p => suite.Spec.Resolve(p!)["x-matchmark-transitions"] as JsonArray ?? []).OfType<JsonObject>());
        var held = true;
        foreach (var rule in rules)
        {
            if (rule["rule"]?.GetValue<string>() is not { } text || Cel.Check(text, after.Json(), before.Json(), hasOld: true) is not { Holds: false } result)
            {
                continue;
            }

            held = false;
            Add(findings, Checks.TransitionRule, subject, after,
                result.Error is null
                    ? $"going from {RegressionReport.Excerpt(before.Body)} to {RegressionReport.Excerpt(after.Body)} breaks the transition rule {text}"
                      + (rule["message"]?.GetValue<string>() is { } message ? $" ({message})" : string.Empty)
                    : $"the transition rule {text} can't be evaluated: {result.Error}",
                result.Error is null ? Severity.Failure : Severity.Warning);
        }

        return held;
    }

    // ---------------------------------------------------------------- metamorphic relations

    /// <summary>
    /// The parameters with a prefix relation: those an <c>x-matchmark-relations</c> names, else the limit of a paged list
    /// (whose first n results should be the start of its first n + 1).
    /// </summary>
    private List<SpecParameter> Prefixes(SpecOperation operation)
    {
        if (options.Skip.Contains(Checks.RelationHolds) || operation.Method != "GET")
        {
            return [];
        }

        var declared = (operation.Json["x-matchmark-relations"] as JsonArray ?? []).OfType<JsonObject>()
            .Select(r => r["prefix"]?.GetValue<string>())
            .Select(n => operation.Parameters.FirstOrDefault(p => p.In == "query" && p.Name == n))
            .Where(p => p is not null).Select(p => p!).ToList();
        return declared.Count > 0 ? declared
            : Paging(operation) is { Limit: { } limit } ? [limit]
            : [];
    }

    /// <summary>Asks for n, then n + 1 results: the first answer must be the start of the second.</summary>
    private async Task<WorkflowResult> PrefixAsync(SpecOperation operation, SpecParameter parameter, CancellationToken cancellationToken)
    {
        var name = $"prefix {operation.Id} by {parameter.Name}";
        var findings = new List<Finding>();
        var exchanges = new List<Exchange>();
        var schema = suite.Spec.Resolve(parameter.Schema);
        var n = Math.Max(SchemaValidator.Number(schema, "minimum") ?? 1, 2);
        if (SchemaValidator.Number(schema, "maximum") is { } maximum && n + 1 > maximum)
        {
            return new WorkflowResult(name, exchanges, findings, $"{parameter.Name} can't be both {n} and {n + 1}");
        }

        var query = new List<KeyValuePair<string, string>>();
        var path = operation.Path;
        foreach (var other in operation.Parameters.Where(p => p.Required && p != parameter))
        {
            if (!suite.Samples.TrySample(other.Schema, false, out var sample))
            {
                return new WorkflowResult(name, exchanges, findings, $"no value for the required {other.Name}");
            }

            var text = ArazzoContext.Text(sample);
            switch (other.In)
            {
                case "path":
                    path = path.Replace($"{{{other.Name}}}", Uri.EscapeDataString(text), StringComparison.Ordinal);
                    break;
                case "query":
                    query.Add(new(other.Name, text));
                    break;
                default:
                    return new WorkflowResult(name, exchanges, findings, $"the required {other.In} parameter {other.Name} isn't sampled here");
            }
        }

        async Task<(Exchange Exchange, JsonArray? Items)> Ask(decimal size)
        {
            var request = new RegressionRequest("GET", path, [.. query, new(parameter.Name, size.ToString(CultureInfo.InvariantCulture))], [], null, null);
            var exchange = await SendAsync(request, cancellationToken).ConfigureAwait(false);
            exchanges.Add(exchange);
            Response(operation, exchange, requestValid: true, name, findings);
            return (exchange, Items(operation, exchange.Json()));
        }

        var (first, few) = await Ask(n).ConfigureAwait(false);
        var (second, more) = await Ask(n + 1).ConfigureAwait(false);
        if (few is null || more is null)
        {
            return new WorkflowResult(name, exchanges, Noticed(findings), $"gave {first.Status} and {second.Status}, without a list to compare");
        }

        var a = few.Select(Key).ToList();
        var b = more.Select(Key).ToList();
        if (a.Count > n || b.Count > n + 1)
        {
            Add(findings, Checks.RelationHolds, name, a.Count > n ? first : second,
                $"asked for at most {n} and {n + 1}, given {a.Count} and {b.Count}");
        }
        else if (!b.Take(a.Count).SequenceEqual(a) || a.Count == n && b.Count < n)
        {
            Add(findings, Checks.RelationHolds, name, second,
                $"the first {n} ({string.Join(", ", a.Select(Excerpt))}) aren't the start of the first {n + 1} ({string.Join(", ", b.Select(Excerpt))})");
        }

        return new WorkflowResult(name, exchanges, Noticed(findings), null);
    }

    /// <summary>A list response's items: the array itself, the paging items, or its one array property.</summary>
    private JsonArray? Items(SpecOperation operation, JsonNode? body)
    {
        if (body is JsonArray array)
        {
            return array;
        }

        if (body is not JsonObject obj)
        {
            return null;
        }

        if (Paging(operation) is { } paging)
        {
            return obj[paging.Items] as JsonArray;
        }

        var arrays = obj.Where(p => p.Value is JsonArray).ToList();
        return arrays.Count == 1 ? (JsonArray)arrays[0].Value! : null;
    }
}
