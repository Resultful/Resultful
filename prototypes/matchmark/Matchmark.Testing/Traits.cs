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

    /// <summary>The statuses a reference to nothing answers with, in the order they're looked for in the spec.</summary>
    public static readonly int[] ReferenceStatuses = [422, 404, 400];

    /// <summary>The statuses a delete refused while something still refers to the resource answers with.</summary>
    public static readonly int[] RestrictStatuses = [409, 422];

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
                    key["scope"]?.GetValue<string>() ?? "collection", key["nulls"]?.GetValue<string>() == "equal") { Declared = DeclaredStatus(key) });
            }
        }

        foreach (var part in (schema["allOf"] as JsonArray ?? []).OfType<JsonNode>())
        {
            keys.AddRange(Unique(spec, part));
        }

        return keys;
    }

    /// <summary>The references declared on a schema (<c>x-matchmark-references</c>), by field, its <c>allOf</c> parts included.</summary>
    public static IReadOnlyList<Reference> References(OpenApiSpec spec, JsonNode? schemaNode)
    {
        if (schemaNode is null)
        {
            return [];
        }

        var schema = spec.Resolve(schemaNode);
        var references = new List<Reference>();
        foreach (var (field, node) in schema["x-matchmark-references"] as JsonObject ?? [])
        {
            if (node is JsonObject reference && reference["target"]?.GetValue<string>() is { } target
                && System.Text.RegularExpressions.Regex.Match(target, @"^(?<collection>.+)/\{[^}]+\}$") is { Success: true } match)
            {
                references.Add(new Reference(field, target, match.Groups["collection"].Value, reference["onDelete"]?.GetValue<string>() ?? "restrict",
                    reference["required"]?.GetValue<bool>() != false) { Declared = DeclaredStatus(reference) });
            }
        }

        foreach (var part in (schema["allOf"] as JsonArray ?? []).OfType<JsonNode>())
        {
            references.AddRange(References(spec, part));
        }

        return references;
    }

    /// <summary>A status the declaration names (<c>status: 400</c>), which wins over the order statuses are looked for in.</summary>
    private static int? DeclaredStatus(JsonObject declaration)
        => declaration["status"] is JsonValue status && status.GetValueKind() == JsonValueKind.Number ? status.GetValue<int>() : null;

    /// <summary>The resource an operation writes or reads: its success response's schema, else its request body's.</summary>
    public static JsonNode? ResourceSchema(SpecOperation operation)
        => operation.Responses.Where(r => r.Key.StartsWith('2')).OrderBy(r => r.Key, StringComparer.Ordinal)
               .Select(r => r.Value.Content.Values.FirstOrDefault()).FirstOrDefault(s => s is not null)
           ?? operation.Body?.Schema;

    /// <summary>
    /// The status a violation answers with: the one the declaration names, else the first of the statuses the operation
    /// documents, else the first in the list.
    /// </summary>
    public static int Status(SpecOperation operation, IReadOnlyList<int> statuses, int? declared = null)
        => declared ?? statuses.FirstOrDefault(s => operation.Response(s) is not null, statuses[0]);

    /// <summary>A resource's value for a field (a dotted path), as text for comparing with ids; null when absent.</summary>
    public static string? Text(JsonObject resource, string field)
    {
        JsonNode? node = resource;
        foreach (var segment in field.Split('.'))
        {
            node = node?[segment];
        }

        return node is null ? null : node.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : node.ToJsonString();
    }

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
    /// <summary>The status the declaration names for a violation, if any.</summary>
    public int? Declared { get; init; }

    public override string ToString() => string.Join(", ", Fields) + (IgnoreCase ? " (ignoring case)" : string.Empty);
}

/// <summary>
/// A reference: a field whose value is the id of a resource in another collection (its item path, <c>/accounts/{id}</c>),
/// what a delete of that resource does to it (restrict, cascade, clear, none), and whether it must be set.
/// </summary>
public sealed record Reference(string Field, string Target, string Collection, string OnDelete, bool Required)
{
    /// <summary>The status the declaration names for a reference to nothing, if any.</summary>
    public int? Declared { get; init; }

    public override string ToString() => $"{Field} → {Target}";
}
