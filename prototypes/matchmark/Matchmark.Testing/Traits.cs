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

    /// <summary>The statuses a read, change or delete of another principal's resource answers with, in the order they're looked for.</summary>
    public static readonly int[] ScopedStatuses = [404, 403];

    /// <summary>The scope declared on a schema (<c>x-matchmark-scoped</c>), its <c>allOf</c> parts included; null when none.</summary>
    public static Scoped? ScopedOf(OpenApiSpec spec, JsonNode? schemaNode)
    {
        if (schemaNode is null)
        {
            return null;
        }

        var schema = spec.Resolve(schemaNode);
        if (schema["x-matchmark-scoped"] is JsonObject scoped)
        {
            return new Scoped(scoped["by"]?.GetValue<string>() ?? "principal", scoped["visible"]?.GetValue<string>() ?? "owner") { Declared = DeclaredStatus(scoped) };
        }

        return (schema["allOf"] as JsonArray ?? []).OfType<JsonNode>().Select(part => ScopedOf(spec, part)).FirstOrDefault(s => s is not null);
    }

    /// <summary>
    /// The principal a request is from: the credential it presents where the spec's first security scheme says (an API
    /// key's header, query parameter or cookie), else its Authorization header; empty when it presents none.
    /// </summary>
    public static string Principal(OpenApiSpec spec, IReadOnlyDictionary<string, string> headers, IEnumerable<KeyValuePair<string, string>> query)
    {
        var scheme = (spec.Find("#/components/securitySchemes") as JsonObject)?.Select(s => s.Value).OfType<JsonObject>().FirstOrDefault();
        if (scheme?["type"]?.GetValue<string>() == "apiKey" && scheme["name"]?.GetValue<string>() is { } name)
        {
            switch (scheme["in"]?.GetValue<string>())
            {
                case "header" when headers.TryGetValue(name, out var key):
                    return key;
                case "query" when query.FirstOrDefault(q => q.Key == name).Value is { } key:
                    return key;
                case "cookie" when headers.TryGetValue("Cookie", out var cookies):
                    return cookies.Split(';').Select(c => c.Trim().Split('=', 2)).FirstOrDefault(c => c.Length == 2 && c[0] == name)?[1] ?? string.Empty;
            }
        }

        return headers.GetValueOrDefault("Authorization") ?? string.Empty;
    }

    /// <summary>The schema a collection holds: what its create returns, else what its item read returns.</summary>
    public static JsonNode? CollectionSchema(OpenApiSpec spec, string collection)
        => spec.Operations.Where(o => o.Method == "POST" && o.Path == collection).Select(ResourceSchema).FirstOrDefault(s => s is not null)
           ?? spec.Operations.Where(o => o.Method == "GET" && o.Path.StartsWith(collection + "/{", StringComparison.Ordinal)).Select(ResourceSchema).FirstOrDefault(s => s is not null);

    /// <summary>The statuses a create past a limit answers with, in the order they're looked for in the spec.</summary>
    public static readonly int[] LimitStatuses = [409, 422, 400];

    /// <summary>The limit declared on a schema (<c>x-matchmark-limit</c>), its <c>allOf</c> parts included; null when none.</summary>
    public static Limit? LimitOf(OpenApiSpec spec, JsonNode? schemaNode)
    {
        if (schemaNode is null)
        {
            return null;
        }

        var schema = spec.Resolve(schemaNode);
        if (schema["x-matchmark-limit"] is JsonObject limit && limit["max"] is JsonValue max && max.GetValueKind() == JsonValueKind.Number)
        {
            return new Limit(max.GetValue<int>(), limit["scope"]?.GetValue<string>() ?? "collection") { Declared = DeclaredStatus(limit) };
        }

        return (schema["allOf"] as JsonArray ?? []).OfType<JsonNode>().Select(part => LimitOf(spec, part)).FirstOrDefault(l => l is not null);
    }

    /// <summary>
    /// The totals declared conserved (<c>x-matchmark-conserved</c>) on every component schema, each with the collection
    /// that holds the schema's resources; a schema no collection creates is left out.
    /// </summary>
    public static IReadOnlyList<Conserved> ConservedTotals(OpenApiSpec spec)
    {
        var totals = new List<Conserved>();
        foreach (var (name, schema) in spec.Schemas)
        {
            if (Collection(spec, name) is not { } collection)
            {
                continue;
            }

            foreach (var declaration in (schema["x-matchmark-conserved"] as JsonArray ?? []).OfType<JsonObject>())
            {
                if (declaration["total"]?.GetValue<string>() is { } total)
                {
                    totals.Add(new Conserved(name, total, collection,
                        (declaration["except"] as JsonArray ?? []).Select(e => e?.GetValue<string>()).OfType<string>().ToList()));
                }
            }
        }

        return totals;
    }

    /// <summary>The collection path a component schema's resources live in: where a POST creates it, else where a GET lists it.</summary>
    public static string? Collection(OpenApiSpec spec, string schema)
    {
        var reference = "#/components/schemas/" + schema;
        bool Refers(JsonNode? node) => node?["$ref"]?.GetValue<string>() == reference || node?["items"]?["$ref"]?.GetValue<string>() == reference;
        bool HasItemPath(string path) => spec.Operations.Any(o => System.Text.RegularExpressions.Regex.IsMatch(o.Path,
            "^" + System.Text.RegularExpressions.Regex.Escape(path.TrimEnd('/')) + @"/\{[^}]+\}$"));
        return spec.Operations.FirstOrDefault(o => o.Method == "POST" && HasItemPath(o.Path) && Refers(ResourceSchema(o)))?.Path
               ?? spec.Operations.FirstOrDefault(o => o.Method == "GET" && HasItemPath(o.Path) && Refers(ResourceSchema(o)))?.Path;
    }

    /// <summary>A field's sum over resources: numbers, and decimal strings ("25.00"); anything else counts as nothing.</summary>
    public static decimal Sum(IEnumerable<JsonObject> resources, string field)
        => resources.Sum(r => Node(r, field) switch
        {
            JsonValue v when v.GetValueKind() == JsonValueKind.String
                && decimal.TryParse(v.GetValue<string>(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) => d,
            var n => SchemaValidator.NumberOf(n) ?? 0m,
        });

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

    /// <summary>The derived fields declared on a schema (<c>x-matchmark-derived</c>): field, when (create, update, always), CEL expression.</summary>
    public static IReadOnlyList<Derived> DerivedFields(OpenApiSpec spec, JsonNode? schemaNode)
    {
        if (schemaNode is null)
        {
            return [];
        }

        var schema = spec.Resolve(schemaNode);
        var derived = (schema["x-matchmark-derived"] as JsonObject ?? []).Where(p => p.Value?["value"] is JsonValue)
            .Select(p => new Derived(p.Key, p.Value!["on"]?.GetValue<string>() ?? "create", p.Value!["value"]!.GetValue<string>())).ToList();
        foreach (var part in (schema["allOf"] as JsonArray ?? []).OfType<JsonNode>())
        {
            derived.AddRange(DerivedFields(spec, part));
        }

        return derived;
    }

    /// <summary>A list operation's declared order (<c>x-matchmark-order</c>): fields, each ascending unless marked <c>desc</c>.</summary>
    public static IReadOnlyList<(string Field, bool Descending)> Order(SpecOperation operation)
        => (operation.Json["x-matchmark-order"]?["by"] as JsonArray ?? []).Select(b => b?.GetValue<string>()).OfType<string>()
            .Select(b => b.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Select(parts => (parts[0], parts.Length > 1 && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase))).ToList();

    /// <summary>The query parameter that chooses a list's order (sort, orderBy, order_by), when the operation has one; null otherwise.</summary>
    public static SpecParameter? SortParameter(SpecOperation operation)
        => operation.Parameters.FirstOrDefault(p => p.In == "query" && p.Name.ToLowerInvariant() is "sort" or "orderby" or "order_by");

    /// <summary>
    /// The order a sort value asks for: a field, descending when it starts with '-' or ends with " desc", several joined
    /// by commas (<c>-createdAt,id</c>); empty when there's no value.
    /// </summary>
    public static IReadOnlyList<(string Field, bool Descending)> SortOrder(string? value)
        => (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.StartsWith('-') ? (part[1..], true)
                : part.EndsWith(" desc", StringComparison.OrdinalIgnoreCase) ? (part[..^5].Trim(), true)
                : (part.EndsWith(" asc", StringComparison.OrdinalIgnoreCase) ? part[..^4].Trim() : part, false))
            .ToList();

    /// <summary>Compares two resources by a declared order; numbers as numbers, strings ordinally, a missing value first.</summary>
    public static int Compare(JsonObject a, JsonObject b, IReadOnlyList<(string Field, bool Descending)> order)
    {
        foreach (var (field, descending) in order)
        {
            var x = Node(a, field);
            var y = Node(b, field);
            var result = x is null && y is null ? 0 : x is null ? -1 : y is null ? 1
                : SchemaValidator.NumberOf(x) is { } m && SchemaValidator.NumberOf(y) is { } n ? m.CompareTo(n)
                : string.CompareOrdinal(Text(a, field), Text(b, field));
            if (result != 0)
            {
                return descending ? -result : result;
            }
        }

        return 0;
    }

    /// <summary>A derived field's value: the expression over the request, the resource and its earlier reading; null when it can't be evaluated.</summary>
    public static JsonNode? Evaluate(Derived derived, JsonNode? request, JsonObject self, JsonObject? oldSelf)
    {
        var variables = new Dictionary<string, JsonNode?>(StringComparer.Ordinal) { ["request"] = request?.DeepClone() ?? new JsonObject(), ["self"] = self.DeepClone() };
        if (oldSelf is not null)
        {
            variables["oldSelf"] = oldSelf.DeepClone();
        }

        var (value, error) = Matchmark.Rules.Cel.Evaluate(derived.Expression, variables);
        return error is null ? value : null;
    }

    private static JsonNode? Node(JsonObject resource, string field)
    {
        JsonNode? node = resource;
        foreach (var segment in field.Split('.'))
        {
            node = node?[segment];
        }

        return node;
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

/// <summary>A scope: a resource belongs to the principal that created it, and is visible to its owner only.</summary>
public sealed record Scoped(string By, string Visible)
{
    /// <summary>The status the declaration names for another principal's read, change or delete, if any.</summary>
    public int? Declared { get; init; }
}

/// <summary>A limit: at most <paramref name="Max"/> resources in the scope (collection, parent, or principal).</summary>
public sealed record Limit(int Max, string Scope)
{
    /// <summary>The status the declaration names for a create past the limit, if any.</summary>
    public int? Declared { get; init; }

    public override string ToString() => $"{Max} per {Scope}";
}

/// <summary>
/// A conserved total: the field summed over the schema's collection, unchanged by every successful operation but those
/// in <paramref name="Except"/>.
/// </summary>
public sealed record Conserved(string Schema, string Total, string Collection, IReadOnlyList<string> Except)
{
    public override string ToString() => $"the total of {Schema}.{Total}";
}

/// <summary>A derived field: its name, when it's set (create, update, always), and the CEL expression over request, self and oldSelf.</summary>
public sealed record Derived(string Field, string On, string Expression)
{
    public bool OnCreate => On is "create" or "always";

    public bool OnUpdate => On is "update" or "always";
}
