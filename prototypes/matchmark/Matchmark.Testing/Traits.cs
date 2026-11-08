using System.Text.Json;
using System.Text.Json.Nodes;

namespace Matchmark.Testing;

/// <summary>
/// Invariants across a collection (docs/reference/invariants.md, decision Q44): traits declared on a resource's schema,
/// read once here and used by the twin, which enforces them, and the suite, which checks the real service does.
/// </summary>
public static class Traits
{
    /// <summary>The statuses a unique-key violation answers with, in the order they're looked for in the spec.</summary>
    public static readonly int[] UniqueStatuses = [409, 422, 400];

    /// <summary>The unique keys declared on a schema (<c>x-matchmark-unique</c>), its <c>allOf</c> parts included.</summary>
    public static IReadOnlyList<UniqueKey> Unique(OpenApiSpec spec, JsonNode? schemaNode)
    {
        if (schemaNode is null)
        {
            return [];
        }

        var schema = spec.Resolve(schemaNode);
        var keys = new List<UniqueKey>();
        foreach (var key in (schema["x-matchmark-unique"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var fields = (key["fields"] as JsonArray ?? []).Select(f => f?.GetValue<string>()).OfType<string>().ToList();
            if (fields.Count > 0)
            {
                keys.Add(new UniqueKey(fields, key["ignoreCase"]?.GetValue<bool>() == true,
                    key["scope"]?.GetValue<string>() ?? "collection", key["nulls"]?.GetValue<string>() == "equal"));
            }
        }

        foreach (var part in (schema["allOf"] as JsonArray ?? []).OfType<JsonNode>())
        {
            keys.AddRange(Unique(spec, part));
        }

        return keys;
    }

    /// <summary>The resource an operation writes or reads: its success response's schema, else its request body's.</summary>
    public static JsonNode? ResourceSchema(SpecOperation operation)
        => operation.Responses.Where(r => r.Key.StartsWith('2')).OrderBy(r => r.Key, StringComparer.Ordinal)
               .Select(r => r.Value.Content.Values.FirstOrDefault()).FirstOrDefault(s => s is not null)
           ?? operation.Body?.Schema;

    /// <summary>The first of the statuses the operation documents; the first in the list when it documents none.</summary>
    public static int Status(SpecOperation operation, IReadOnlyList<int> statuses)
        => statuses.FirstOrDefault(s => operation.Response(s) is not null, statuses[0]);

    /// <summary>The key's values in a resource, or null when one is null or absent and nulls are distinct (it can't collide).</summary>
    public static string? Value(UniqueKey key, JsonObject resource)
    {
        var parts = new List<string>();
        foreach (var field in key.Fields)
        {
            JsonNode? node = resource;
            foreach (var segment in field.Split('.'))
            {
                node = node?[segment];
            }

            if (node is null && !key.NullsEqual)
            {
                return null;
            }

            var text = node is null ? "null"
                : node.GetValueKind() == JsonValueKind.String ? node.GetValue<string>()
                : node.ToJsonString();
            parts.Add(key.IgnoreCase ? text.ToUpperInvariant() : text);
        }

        return string.Join('\u001f', parts);
    }

    /// <summary>The first key another resource already holds, or null.</summary>
    public static UniqueKey? Collision(IReadOnlyList<UniqueKey> keys, JsonObject resource, IEnumerable<JsonObject> others)
    {
        var list = others.ToList();
        return keys.FirstOrDefault(key => Value(key, resource) is { } value && list.Any(other => Value(key, other) == value));
    }
}

/// <summary>One unique key: its fields (dotted paths), and how values compare.</summary>
public sealed record UniqueKey(IReadOnlyList<string> Fields, bool IgnoreCase, string Scope, bool NullsEqual)
{
    public override string ToString() => string.Join(", ", Fields) + (IgnoreCase ? " (ignoring case)" : string.Empty);
}
