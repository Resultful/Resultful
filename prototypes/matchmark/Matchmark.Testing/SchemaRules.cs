using System.Text.Json.Nodes;
using Matchmark.Rules;

namespace Matchmark.Testing;

/// <summary>The x-matchmark-rules (CEL over <c>self</c>) of every schema a value is an instance of: itself, its properties, items and allOf.</summary>
internal static class SchemaRules
{
    /// <summary>Each rule the value breaks, at its path (<c>$</c>, <c>$.items[0]</c>), or can't be evaluated against (with the error).</summary>
    public static IEnumerable<(string Path, string Rule, string? Message, string? Error)> Broken(OpenApiSpec spec, JsonNode? value, JsonNode schema)
    {
        var broken = new List<(string, string, string?, string?)>();
        Walk(spec, value, schema, "$", 0, broken);
        return broken;
    }

    private static void Walk(OpenApiSpec spec, JsonNode? value, JsonNode schema, string path, int depth, List<(string, string, string?, string?)> broken)
    {
        if (depth > 32)
        {
            return;
        }

        var resolved = spec.Resolve(schema);
        foreach (var rule in (resolved["x-matchmark-rules"] as JsonArray ?? []).OfType<JsonObject>())
        {
            if (rule["rule"]?.GetValue<string>() is { } text && Cel.Check(text, value) is { Holds: false } result)
            {
                broken.Add((path, text, rule["message"]?.GetValue<string>(), result.Error));
            }
        }

        foreach (var part in (resolved["allOf"] as JsonArray ?? []).Where(p => p is not null))
        {
            Walk(spec, value, part!, path, depth + 1, broken);
        }

        switch (value)
        {
            case JsonObject obj when resolved["properties"] is JsonObject properties:
                foreach (var (name, property) in properties)
                {
                    if (property is not null && obj.TryGetPropertyValue(name, out var child))
                    {
                        Walk(spec, child, property, $"{path}.{name}", depth + 1, broken);
                    }
                }

                break;
            case JsonArray array when resolved["items"] is { } items:
                for (var i = 0; i < array.Count; i++)
                {
                    Walk(spec, array[i], items, $"{path}[{i}]", depth + 1, broken);
                }

                break;
        }
    }
}
