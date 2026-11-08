using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Matchmark.Testing;

/// <summary>One changed copy of a value: what changed, where, and the whole value after the change.</summary>
public sealed record Variant(string Path, string Description, JsonNode? Value);

/// <summary>
/// Values built from schemas: a valid sample, valid values at every bound, and values that break exactly one constraint.
/// Everything is deterministic, so a regression suite generates the same cases on every run.
/// </summary>
public sealed class Samples(OpenApiSpec spec, SchemaValidator validator)
{
    private static readonly string[] Words = ["sample", "a", "abc", "a-b", "Sample", "A", "x1", "123", "sample-1", "abc123", "A1"];
    private static readonly string[] NonMatching = ["!!", "NOT VALID", " ", "~", "-1", "0", "Aa1!"];

    /// <summary>A valid value: the schema's example or default if valid, else one built from the schema.</summary>
    public bool TrySample(JsonNode schemaNode, bool full, out JsonNode? value) => TrySample(schemaNode, full, Direction.Request, out value);

    /// <summary>
    /// A valid value for one direction: a request leaves read-only values out (the server sets them), a response leaves
    /// write-only values out (they're never sent back).
    /// </summary>
    public bool TrySample(JsonNode schemaNode, bool full, Direction direction, out JsonNode? value)
    {
        value = Build(schemaNode, full, 0, string.Empty, _ => 0, out var ok, direction: direction);
        return ok && validator.IsValid(value, schemaNode, direction);
    }

    /// <summary>
    /// Valid samples (every property) that between them choose each member of every oneOf and anyOf: the first sample
    /// takes the first member everywhere, and each further one takes another member at one place, labelled "(cat)".
    /// </summary>
    public IReadOnlyList<(string Label, JsonNode? Value)> Alternatives(JsonNode schemaNode)
    {
        var unions = new List<(string Path, int Members, List<string> Labels)>();
        var first = Build(schemaNode, true, 0, string.Empty, path => 0, out var ok, unions);
        var samples = new List<(string, JsonNode?)>();
        if (!ok || !validator.IsValid(first, schemaNode, Direction.Request))
        {
            return samples;
        }

        samples.Add((string.Empty, first));
        foreach (var (path, members, labels) in unions.DistinctBy(u => u.Path))
        {
            for (var k = 1; k < members; k++)
            {
                var choice = k;
                var value = Build(schemaNode, true, 0, string.Empty, p => p == path ? choice : 0, out var memberOk);
                if (memberOk && validator.IsValid(value, schemaNode, Direction.Request))
                {
                    samples.Add(($" ({labels[k]})", value));
                }
            }
        }

        return samples;
    }

    private JsonNode? Build(JsonNode schemaNode, bool full, int depth, string path, Func<string, int> choose, out bool ok,
        List<(string Path, int Members, List<string> Labels)>? unions = null, Direction direction = Direction.Request)
    {
        ok = true;
        var schema = spec.Resolve(schemaNode);
        foreach (var candidate in new[] { schema["example"], (schema["examples"] as JsonArray)?.FirstOrDefault(), schema["default"], schema["const"] })
        {
            if (candidate is not null && validator.IsValid(candidate, schema, Direction.Request))
            {
                return candidate.DeepClone();
            }
        }

        if (schema["enum"] is JsonArray values && values.Count > 0)
        {
            return values[0]?.DeepClone();
        }

        if (depth > 16)
        {
            ok = false;
            return null;
        }

        if (schema["oneOf"] is JsonArray one && one.Count > 0 || schema["anyOf"] is JsonArray any && any.Count > 0)
        {
            var members = (JsonArray)(schema["oneOf"] ?? schema["anyOf"])!;
            var mapping = SchemaValidator.Mapping(schema, members);
            unions?.Add((path, mapping.Count, mapping.Select(m => m.Value).ToList()));
            var (value, member) = mapping[Math.Clamp(choose(path), 0, mapping.Count - 1)];
            var built = Build(member, full, depth + 1, path, choose, out ok, unions, direction);
            if (schema["discriminator"]?["propertyName"]?.GetValue<string>() is { } property && built is JsonObject obj)
            {
                obj[property] = value;
            }

            return built;
        }

        if (schema["allOf"] is JsonArray all)
        {
            var merged = new JsonObject();
            foreach (var part in all)
            {
                if (Build(part!, full, depth + 1, path, choose, out var partOk, unions, direction) is JsonObject piece && partOk)
                {
                    foreach (var (k, v) in piece)
                    {
                        merged[k] = v?.DeepClone();
                    }
                }
            }

            return merged;
        }

        var types = SchemaValidator.Types(schema);
        var type = types.FirstOrDefault(t => t != "null") ?? (schema["properties"] is not null ? "object" : types.FirstOrDefault() ?? "object");
        switch (type)
        {
            case "null":
                return null;
            case "boolean":
                return JsonValue.Create(true);
            case "integer":
            case "number":
                return JsonValue.Create(Within(schema, type == "integer", 1m));
            case "string":
                var text = Text(schema, null);
                ok = text is not null;
                return text is null ? null : JsonValue.Create(text);
            case "array":
                var array = new JsonArray();
                var count = Math.Max(SchemaValidator.Count(schema, "minItems") ?? 1, 1);
                count = Math.Min(count, SchemaValidator.Count(schema, "maxItems") ?? count);
                for (var i = 0; i < count && schema["items"] is { } items; i++)
                {
                    array.Add(Build(items, full, depth + 1, $"{path}[]", choose, out var itemOk, unions, direction));
                    ok &= itemOk;
                }

                return array;
            default:
                var result = new JsonObject();
                var required = (schema["required"] as JsonArray ?? []).Select(r => r!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
                foreach (var (name, property) in schema["properties"]?.AsObject() ?? [])
                {
                    // Read-only values are the server's to set; a request doesn't send them (the suite's read-only case does, on purpose).
                    // Write-only values are never sent back, so a response leaves them out.
                    if (!required.Contains(name) && !full || validator.Flag(property!, direction == Direction.Response ? "writeOnly" : "readOnly"))
                    {
                        continue;
                    }

                    var built = Build(property!, full, depth + 1, SchemaValidator.Join(path, name), choose, out var propertyOk, unions, direction);
                    if (propertyOk)
                    {
                        result[name] = built;
                    }
                    else if (required.Contains(name))
                    {
                        ok = false;
                    }
                }

                return result;
        }
    }

    /// <summary>A number inside the schema's bounds, as close to <paramref name="preferred"/> as they allow.</summary>
    private static decimal Within(JsonObject schema, bool integer, decimal preferred)
    {
        var value = preferred;
        var step = integer ? 1m : 0.5m;
        if (SchemaValidator.Number(schema, "minimum") is { } min && value < min) value = min;
        if (SchemaValidator.ExclusiveBound(schema, "exclusiveMinimum", "minimum") is { } xmin && value <= xmin) value = xmin + step;
        if (SchemaValidator.Number(schema, "maximum") is { } max && value > max) value = max;
        if (SchemaValidator.ExclusiveBound(schema, "exclusiveMaximum", "maximum") is { } xmax && value >= xmax) value = xmax - step;
        if (SchemaValidator.Number(schema, "multipleOf") is { } m && m != 0 && value % m != 0) value = Math.Ceiling(value / m) * m;
        return integer ? Math.Round(value) : value;
    }

    /// <summary>A string the schema accepts, optionally of an exact length; null when none of the candidates fit.</summary>
    public static string? Text(JsonObject schema, int? length)
    {
        var format = schema["format"]?.GetValue<string>();
        var formatted = format switch
        {
            "date-time" => "2024-01-02T03:04:05Z",
            "date" => "2024-01-02",
            "uuid" => "00000000-0000-4000-8000-000000000001",
            "email" => "someone@example.com",
            "uri" => "https://example.com/",
            _ => null,
        };
        if (formatted is not null)
        {
            return length is null || formatted.Length == length ? formatted : null;
        }

        var min = SchemaValidator.Count(schema, "minLength") ?? 0;
        var max = SchemaValidator.Count(schema, "maxLength") ?? int.MaxValue;
        var pattern = schema["pattern"]?.GetValue<string>() is { } p ? SchemaValidator.Pattern(p) : null;
        foreach (var word in Words)
        {
            var text = Fit(word, length ?? Math.Clamp(word.Length, min, max));
            if (text.Length >= min && text.Length <= max && (pattern is null || pattern.IsMatch(text)))
            {
                return text;
            }
        }

        return null;
    }

    /// <summary>The word cut or padded (repeating its last character) to a length.</summary>
    private static string Fit(string word, int length)
        => length <= word.Length ? word[..length] : word + new string(word[^1], length - word.Length);

    // ---------------------------------------------------------------- variants

    /// <summary>Valid copies of <paramref name="root"/> with one value moved to each bound its schema has.</summary>
    public IEnumerable<Variant> Boundaries(JsonNode? root, JsonNode schema)
        => Walk(root, schema, string.Empty, Bounds).Where(v => validator.IsValid(v.Value, schema, Direction.Request)).DistinctBy(v => v.Value?.ToJsonString());

    /// <summary>Copies of <paramref name="root"/> that each break exactly one constraint, and so must be rejected.</summary>
    public IEnumerable<Variant> Violations(JsonNode? root, JsonNode schema)
        => Walk(root, schema, string.Empty, Breaks).Where(v => !validator.IsValid(v.Value, schema, Direction.Request)).DistinctBy(v => v.Value?.ToJsonString());

    private delegate IEnumerable<(string Description, JsonNode? Value)> Leaf(JsonNode? value, JsonObject schema);

    /// <summary>
    /// Every variant of the value: the leaf function applied here, then (for objects) a required property removed or
    /// each property varied in turn, and (for arrays) the first item varied. Each variant is the whole value.
    /// </summary>
    private IEnumerable<Variant> Walk(JsonNode? value, JsonNode schemaNode, string path, Leaf leaf, int depth = 0)
    {
        var breaking = leaf == (Leaf)Breaks;
        if (depth > 16)
        {
            yield break;
        }

        var schema = Effective(value, spec.Resolve(schemaNode), path, out var discriminator);
        foreach (var (description, changed) in leaf(value, schema))
        {
            yield return new Variant(path, description, changed);
        }

        if (discriminator is { } d && value is JsonObject tagged && breaking)
        {
            var unknown = (JsonObject)tagged.DeepClone();
            unknown[d.Property] = "not-a-kind";
            yield return new Variant(SchemaValidator.Join(path, d.Property), $"not one of {d.Values}", unknown);
            var missing = (JsonObject)tagged.DeepClone();
            missing.Remove(d.Property);
            yield return new Variant(SchemaValidator.Join(path, d.Property), "missing", missing);
        }

        if (value is JsonObject obj)
        {
            var properties = schema["properties"]?.AsObject();
            if (breaking)
            {
                foreach (var name in (schema["required"] as JsonArray ?? []).Select(r => r!.GetValue<string>()).Where(obj.ContainsKey))
                {
                    if (name == discriminator?.Property)
                    {
                        continue;
                    }

                    var without = (JsonObject)obj.DeepClone();
                    without.Remove(name);
                    yield return new Variant(SchemaValidator.Join(path, name), "missing", without);
                }

                // additionalProperties: false promises an unknown property is refused.
                if (schema["additionalProperties"]?.GetValueKind() == JsonValueKind.False && properties?.ContainsKey("unexpected") != true)
                {
                    var extra = (JsonObject)obj.DeepClone();
                    extra["unexpected"] = "a property the schema doesn't allow";
                    yield return new Variant(SchemaValidator.Join(path, "unexpected"), "with a property additionalProperties: false forbids", extra);
                }
            }

            foreach (var (name, child) in obj.ToList())
            {
                if (properties?[name] is not { } propertySchema || name == discriminator?.Property)
                {
                    continue;
                }

                foreach (var variant in Walk(child, propertySchema, SchemaValidator.Join(path, name), leaf, depth + 1))
                {
                    var copy = (JsonObject)obj.DeepClone();
                    copy[name] = variant.Value?.DeepClone();
                    yield return variant with { Value = copy };
                }
            }
        }
        else if (value is JsonArray array && array.Count > 0 && schema["items"] is { } items)
        {
            foreach (var variant in Walk(array[0], items, $"{path}[0]", leaf, depth + 1))
            {
                var copy = (JsonArray)array.DeepClone();
                copy[0] = variant.Value?.DeepClone();
                yield return variant with { Value = copy };
            }
        }
    }

    /// <summary>For a discriminated oneOf, the member the value says it is (and the discriminator); else the schema.</summary>
    private JsonObject Effective(JsonNode? value, JsonObject schema, string path, out (string Property, string Values)? discriminator)
    {
        discriminator = null;
        if (schema["oneOf"] is JsonArray members && schema["discriminator"]?["propertyName"]?.GetValue<string>() is { } property)
        {
            var mapping = SchemaValidator.Mapping(schema, members);
            discriminator = (property, string.Join(", ", mapping.Select(m => $"'{m.Value}'")));
            var kind = (value as JsonObject)?[property]?.GetValue<string>();
            return mapping.Where(m => m.Value == kind).Select(m => spec.Resolve(m.Member)).FirstOrDefault() ?? schema;
        }

        return schema;
    }

    private IEnumerable<(string, JsonNode?)> Bounds(JsonNode? value, JsonObject schema)
    {
        switch (value?.GetValueKind())
        {
            case JsonValueKind.String when schema["format"] is null && schema["enum"] is null:
                if (SchemaValidator.Count(schema, "minLength") is { } min && Text(schema, min) is { } shortest)
                {
                    yield return ($"at its minLength {min}", JsonValue.Create(shortest));
                }

                if (SchemaValidator.Count(schema, "maxLength") is { } max && Text(schema, max) is { } longest)
                {
                    yield return ($"at its maxLength {max}", JsonValue.Create(longest));
                }

                break;
            case JsonValueKind.Number:
                var integer = SchemaValidator.Types(schema).Contains("integer");
                foreach (var (keyword, bound) in new[] { ("minimum", SchemaValidator.Number(schema, "minimum")), ("maximum", SchemaValidator.Number(schema, "maximum")) })
                {
                    if (bound is { } b)
                    {
                        yield return ($"at its {keyword} {b.ToString(CultureInfo.InvariantCulture)}", JsonValue.Create(b));
                    }
                }

                if (SchemaValidator.ExclusiveBound(schema, "exclusiveMinimum", "minimum") is { } xmin)
                {
                    yield return ($"just above its exclusiveMinimum {xmin.ToString(CultureInfo.InvariantCulture)}", JsonValue.Create(xmin + (integer ? 1 : 0.01m)));
                }

                if (SchemaValidator.ExclusiveBound(schema, "exclusiveMaximum", "maximum") is { } xmax)
                {
                    yield return ($"just below its exclusiveMaximum {xmax.ToString(CultureInfo.InvariantCulture)}", JsonValue.Create(xmax - (integer ? 1 : 0.01m)));
                }

                break;
            case JsonValueKind.Array when value is JsonArray array && array.Count > 0:
                if (SchemaValidator.Count(schema, "minItems") is { } fewest && fewest > 0)
                {
                    yield return ($"with its minItems {fewest}", Repeat(array[0], fewest));
                }

                if (SchemaValidator.Count(schema, "maxItems") is { } most && most <= 1000)
                {
                    yield return ($"with its maxItems {most}", Repeat(array[0], most));
                }

                break;
        }

        // An optional nullable value may be null.
        if (value is not null && SchemaValidator.Types(schema).Contains("null"))
        {
            yield return ("null, which the schema allows", null);
        }
    }

    private static IEnumerable<(string, JsonNode?)> Breaks(JsonNode? value, JsonObject schema)
    {
        var types = SchemaValidator.Types(schema);
        if (types.Count > 0)
        {
            if (!types.Contains("null") && value is not null)
            {
                yield return ("null", null);
            }

            var wrong = !types.Contains("string") ? ("a string", (JsonNode?)JsonValue.Create("not-" + types[0]))
                : !types.Contains("number") && !types.Contains("integer") ? ("a number", JsonValue.Create(12345))
                : !types.Contains("boolean") ? ("a boolean", JsonValue.Create(true))
                : ("an object", new JsonObject());
            yield return ($"{wrong.Item1}, not {string.Join(" or ", types)}", wrong.Item2);

            if (types.Contains("integer") && !types.Contains("number"))
            {
                yield return ("1.5, not an integer", JsonValue.Create(1.5m));
            }
        }

        if (schema["enum"] is JsonArray values && values.All(v => v?.GetValueKind() == JsonValueKind.String))
        {
            yield return ($"not one of {string.Join(", ", values.Select(v => v!.ToJsonString()))}", JsonValue.Create("not-a-value"));
        }

        if (value?.GetValueKind() == JsonValueKind.String || types.Contains("string"))
        {
            var text = value?.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : "a";
            if (SchemaValidator.Count(schema, "minLength") is { } min && min > 0)
            {
                yield return ($"shorter than its minLength {min}", JsonValue.Create(Fit(text.Length == 0 ? "a" : text, min - 1)));
            }

            if (SchemaValidator.Count(schema, "maxLength") is { } max && max < 100_000)
            {
                yield return ($"longer than its maxLength {max}", JsonValue.Create(Fit(text.Length == 0 ? "a" : text, max + 1)));
            }

            if (schema["pattern"]?.GetValue<string>() is { } pattern
                && NonMatching.FirstOrDefault(c => !SchemaValidator.Pattern(pattern).IsMatch(c)) is { } bad)
            {
                yield return ($"'{bad}', which doesn't match its pattern {pattern}", JsonValue.Create(bad));
            }

            if (schema["format"]?.GetValue<string>() is "date-time" or "date" or "uuid" or "email" or "uri")
            {
                yield return ($"not a valid {schema["format"]}", JsonValue.Create("not-a-" + schema["format"]));
            }
        }

        if (types.Contains("integer") || types.Contains("number"))
        {
            var integer = types.Contains("integer");
            if (SchemaValidator.Number(schema, "minimum") is { } min)
            {
                yield return ($"below its minimum {min.ToString(CultureInfo.InvariantCulture)}", JsonValue.Create(min - (integer ? 1 : 0.5m)));
            }

            if (SchemaValidator.Number(schema, "maximum") is { } max)
            {
                yield return ($"above its maximum {max.ToString(CultureInfo.InvariantCulture)}", JsonValue.Create(max + (integer ? 1 : 0.5m)));
            }

            if (SchemaValidator.ExclusiveBound(schema, "exclusiveMinimum", "minimum") is { } xmin)
            {
                yield return ($"equal to its exclusiveMinimum {xmin.ToString(CultureInfo.InvariantCulture)}", JsonValue.Create(xmin));
            }

            if (SchemaValidator.ExclusiveBound(schema, "exclusiveMaximum", "maximum") is { } xmax)
            {
                yield return ($"equal to its exclusiveMaximum {xmax.ToString(CultureInfo.InvariantCulture)}", JsonValue.Create(xmax));
            }

            switch (schema["format"]?.GetValue<string>())
            {
                case "int32":
                    yield return ("2147483648, too big for int32", JsonValue.Create(2147483648m));
                    break;
                case "int64":
                    yield return ("9223372036854775808, too big for int64", JsonValue.Create(9223372036854775808m));
                    break;
            }
        }

        if (value is JsonArray array && array.Count > 0)
        {
            if (SchemaValidator.Count(schema, "minItems") is { } fewest && fewest > 0)
            {
                yield return ($"fewer items than its minItems {fewest}", Repeat(array[0], fewest - 1));
            }

            if (SchemaValidator.Count(schema, "maxItems") is { } most && most < 1000)
            {
                yield return ($"more items than its maxItems {most}", Repeat(array[0], most + 1));
            }
        }
    }

    private static JsonArray Repeat(JsonNode? item, int count)
    {
        var array = new JsonArray();
        for (var i = 0; i < count; i++)
        {
            array.Add(item?.DeepClone());
        }

        return array;
    }

    /// <summary>
    /// A copy whose free-text strings carry a suffix unique to <paramref name="n"/>, so repeated creates don't collide on
    /// unique names. A string keeps its value when the suffix would break its schema.
    /// </summary>
    public JsonNode? Unique(JsonNode? value, JsonNode schema, int n, string? except = null)
    {
        if (value is not JsonObject obj)
        {
            return value?.DeepClone();
        }

        var copy = (JsonObject)obj.DeepClone();
        var resolved = Effective(copy, spec.Resolve(schema), string.Empty, out _);
        var suffix = "-" + Letters(n);
        foreach (var (name, child) in copy.ToList())
        {
            if (child?.GetValueKind() != JsonValueKind.String || resolved["properties"]?[name] is not { } propertySchema || name == except)
            {
                continue;
            }

            var property = spec.Resolve(propertySchema);
            if (property["enum"] is not null || property["format"] is not null || property["readOnly"]?.GetValueKind() == JsonValueKind.True)
            {
                continue;
            }

            var original = child.GetValue<string>();
            var max = SchemaValidator.Count(property, "maxLength") ?? int.MaxValue;
            var candidate = (original.Length + suffix.Length > max ? original[..Math.Max(0, max - suffix.Length)] : original) + suffix;
            copy[name] = candidate;
            if (!validator.IsValid(copy[name], property, Direction.Request))
            {
                copy[name] = original;
            }
        }

        return validator.IsValid(copy, schema, Direction.Request) ? copy : obj.DeepClone();
    }

    private static string Letters(int n)
    {
        var letters = string.Empty;
        do
        {
            letters = (char)('a' + n % 26) + letters;
            n /= 26;
        }
        while (n > 0);

        return letters;
    }
}
