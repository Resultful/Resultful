using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Matchmark.Fakes;

/// <summary>
/// An Arazzo workflow as a scenario for the fake: the same document the regression suite runs against the service
/// scripts the fake a consumer is tested against. Its success path, in order: each step is an expected call to its
/// operation, expecting the parameters and body the step sends (an <c>$inputs</c> value as given; a
/// <c>$steps.….outputs</c> value as the fake answered it), and answered with the status its <c>$statusCode</c>
/// criterion names (else 200) and the spec's example or sample. A step running another workflow is that workflow's
/// steps. A step by <c>operationPath</c>, or its <c>onFailure</c> and <c>goto</c>, can't be scripted and is refused.
/// </summary>
public static partial class ArazzoScenario
{
    public static Scenario Build(string arazzoJson, string workflowId, JsonObject? inputs = null)
    {
        var document = JsonNode.Parse(arazzoJson) as JsonObject ?? throw new ArgumentException("The Arazzo document isn't an object.", nameof(arazzoJson));
        var steps = new List<ScenarioStep>();
        Add(document, workflowId, inputs ?? [], steps, 0);
        return new Scenario { Order = ScenarioOrder.InOrder, Steps = steps };
    }

    private static void Add(JsonObject document, string workflowId, JsonObject inputs, List<ScenarioStep> steps, int depth)
    {
        if (depth > 8)
        {
            throw new ArgumentException($"Workflow '{workflowId}' runs workflows more than 8 deep.");
        }

        var workflow = (document["workflows"] as JsonArray ?? []).OfType<JsonObject>().FirstOrDefault(w => (string?)w["workflowId"] == workflowId)
            ?? throw new ArgumentException($"The Arazzo document has no workflow '{workflowId}'.");
        foreach (var step in (workflow["steps"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var stepId = (string?)step["stepId"] ?? $"step{steps.Count + 1}";
            if (step["onSuccess"] is JsonArray { Count: > 0 } onSuccess && onSuccess.OfType<JsonObject>().Any(a => (string?)a["type"] is "goto" or "retry"))
            {
                throw new ArgumentException($"Step '{stepId}' goes to another step on success; only a workflow's straight path can script a fake.");
            }

            var parameters = Parameters(document, step).ToList();
            if ((string?)step["workflowId"] is { } nested)
            {
                var nestedInputs = new JsonObject();
                foreach (var (name, _, value) in parameters)
                {
                    nestedInputs[name] = Substitute(value, inputs);
                }

                Add(document, nested, nestedInputs, steps, depth + 1);
                continue;
            }

            var operation = (string?)step["operationId"]
                ?? throw new ArgumentException($"Step '{stepId}' names its operation by {(step["operationPath"] is null ? "nothing" : "operationPath")}; a scenario needs an operationId.");
            if (operation.Contains('.', StringComparison.Ordinal) && operation.StartsWith("$sourceDescriptions.", StringComparison.Ordinal))
            {
                operation = operation[(operation.LastIndexOf('.') + 1)..];
            }

            var expect = new JsonObject();
            foreach (var (name, _, value) in parameters)
            {
                expect[name] = Substitute(value, inputs);
            }

            if (step["requestBody"] is JsonObject requestBody && requestBody["payload"] is { } payload)
            {
                var body = Substitute(payload, inputs);
                foreach (var replacement in (requestBody["replacements"] as JsonArray ?? []).OfType<JsonObject>())
                {
                    body = Replace(body, (string?)replacement["target"] ?? string.Empty, Substitute(replacement["value"], inputs));
                }

                expect["body"] = body;
            }

            var outputs = (step["outputs"] as JsonObject ?? []).Where(o => o.Value is JsonValue)
                .ToDictionary(o => o.Key, o => o.Value!.GetValue<string>(), StringComparer.Ordinal);
            steps.Add(new ScenarioStep
            {
                Id = stepId,
                Operation = operation,
                Expect = expect.Count == 0 ? null : expect,
                Status = Status(step),
                Outputs = outputs.Count == 0 ? null : outputs,
            });
        }
    }

    /// <summary>The status a <c>$statusCode == n</c> criterion names; 200 when none does.</summary>
    private static int Status(JsonObject step)
        => (step["successCriteria"] as JsonArray ?? []).OfType<JsonObject>()
            .Select(c => StatusCriterion().Match((string?)c["condition"] ?? string.Empty))
            .Where(m => m.Success)
            .Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .DefaultIfEmpty(200)
            .First();

    [GeneratedRegex(@"^\s*\$statusCode\s*==\s*(\d{3})\s*$")]
    private static partial Regex StatusCriterion();

    private static IEnumerable<(string Name, string In, JsonNode? Value)> Parameters(JsonObject document, JsonObject step)
        => (step["parameters"] as JsonArray ?? []).OfType<JsonObject>()
            .Select(p => (string?)p["reference"] is { } reference && Component(document, reference) is JsonObject shared
                ? new JsonObject { ["name"] = shared["name"]?.DeepClone(), ["in"] = shared["in"]?.DeepClone(), ["value"] = (p["value"] ?? shared["value"])?.DeepClone() }
                : p)
            .Where(p => p["name"] is not null)
            .Select(p => ((string)p["name"]!, (string?)p["in"] ?? string.Empty, p["value"]));

    private static JsonNode? Component(JsonObject document, string reference)
    {
        var parts = reference.Split('.');
        return parts.Length == 3 && parts[0] == "$components" ? document["components"]?[parts[1]]?[parts[2]] : null;
    }

    /// <summary>
    /// Inputs substituted now (<c>$inputs.x</c>, and <c>{$inputs.x}</c> inside a string); <c>$steps</c> expressions kept for
    /// the runner to resolve from the fake's answers. Anything else the fake can't know (<c>$response</c> of the call
    /// itself, an input not given) becomes null, which matches anything.
    /// </summary>
    private static JsonNode? Substitute(JsonNode? node, JsonObject inputs)
    {
        switch (node)
        {
            case JsonObject obj:
                var copy = new JsonObject();
                foreach (var (key, value) in obj)
                {
                    copy[key] = Substitute(value, inputs);
                }

                return copy;
            case JsonArray array:
                return new JsonArray(array.Select(n => Substitute(n, inputs)).ToArray());
            case JsonValue value when value.GetValueKind() == JsonValueKind.String:
                var text = value.GetValue<string>();
                if (text.StartsWith("$inputs.", StringComparison.Ordinal))
                {
                    return Input(text, inputs)?.DeepClone();
                }

                if (text.StartsWith("$steps.", StringComparison.Ordinal))
                {
                    return value.DeepClone();
                }

                if (text.StartsWith('$'))
                {
                    return null;
                }

                var unknown = false;
                var interpolated = Embedded().Replace(text, m =>
                {
                    if (m.Groups[1].Value.StartsWith("$steps.", StringComparison.Ordinal))
                    {
                        return m.Value;
                    }

                    var input = m.Groups[1].Value.StartsWith("$inputs.", StringComparison.Ordinal) ? Input(m.Groups[1].Value, inputs) : null;
                    unknown |= input is null;
                    return input is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : input?.ToJsonString() ?? string.Empty;
                });
                return unknown ? null : JsonValue.Create(interpolated);
            default:
                return node?.DeepClone();
        }
    }

    [GeneratedRegex(@"\{(\$[^}]+)\}")]
    private static partial Regex Embedded();

    private static JsonNode? Input(string expression, JsonObject inputs)
    {
        JsonNode? node = inputs;
        foreach (var part in expression["$inputs.".Length..].Split('.'))
        {
            node = (node as JsonObject)?[part];
        }

        return node;
    }

    private static JsonNode? Replace(JsonNode? payload, string target, JsonNode? value)
    {
        var pointer = target.TrimStart('#');
        if (pointer.Length == 0 || pointer == "/")
        {
            return value;
        }

        JsonNode? parent = payload;
        var parts = pointer.Split('/').Skip(1).Select(p => p.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal)).ToList();
        foreach (var part in parts.Take(parts.Count - 1))
        {
            parent = parent switch
            {
                JsonObject o => o[part],
                JsonArray a when int.TryParse(part, CultureInfo.InvariantCulture, out var i) && i < a.Count => a[i],
                _ => null,
            };
        }

        switch (parent)
        {
            case JsonObject o:
                o[parts[^1]] = value;
                break;
            case JsonArray a when int.TryParse(parts[^1], CultureInfo.InvariantCulture, out var index) && index < a.Count:
                a[index] = value;
                break;
        }

        return payload;
    }
}
