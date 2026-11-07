using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Symbolon.Testing;

/// <summary>
/// Validates JSON against the spec's schemas: $ref, type (including null), enum, const, format, string length and pattern,
/// numeric bounds and multipleOf, array items and bounds, object properties, required and additionalProperties, and
/// allOf, anyOf and oneOf (by discriminator when there is one). Every violation is reported at its path:
/// <c>details.livesLeft</c>, <c>[0].id</c>; the body itself is the empty path.
/// </summary>
public sealed class SchemaValidator(OpenApiSpec spec)
{
    private static readonly ConcurrentDictionary<string, Regex> Patterns = new(StringComparer.Ordinal);

    public List<string> Validate(JsonNode? value, JsonNode schema, string path = "")
    {
        var errors = new List<string>();
        Check(value, schema, path, errors, 0);
        return errors;
    }

    public bool IsValid(JsonNode? value, JsonNode schema) => Validate(value, schema).Count == 0;

    private void Check(JsonNode? value, JsonNode schemaNode, string path, List<string> errors, int depth)
    {
        if (depth > 64)
        {
            return;
        }

        var schema = spec.Resolve(schemaNode);
        var at = path.Length == 0 ? "the body" : path;

        if (schema["allOf"] is JsonArray all)
        {
            foreach (var member in all)
            {
                Check(value, member!, path, errors, depth + 1);
            }
        }

        if (schema["anyOf"] is JsonArray any && !any.Any(m => Validate(value, m!, path).Count == 0))
        {
            errors.Add($"{at} matches none of anyOf");
        }

        if (schema["oneOf"] is JsonArray one)
        {
            CheckOneOf(value, schema, one, path, at, errors, depth);
        }

        if (!TypeMatches(value, schema, out var expected))
        {
            errors.Add($"{at} is {Describe(value)}; the schema says {expected}");
            return;
        }

        if (value is null)
        {
            return;
        }

        if (schema["const"] is { } constant && !JsonNode.DeepEquals(constant, value))
        {
            errors.Add($"{at} is {value.ToJsonString()}; the schema says {constant.ToJsonString()}");
        }

        if (schema["enum"] is JsonArray values && !values.Any(v => JsonNode.DeepEquals(v, value)))
        {
            errors.Add($"{at} is {value.ToJsonString()}, which is not one of {string.Join(", ", values.Select(v => v?.ToJsonString()))}");
        }

        switch (value.GetValueKind())
        {
            case JsonValueKind.String:
                CheckString(value.GetValue<string>(), schema, at, errors);
                break;
            case JsonValueKind.Number:
                CheckNumber(value.GetValue<decimal>(), schema, at, errors);
                break;
            case JsonValueKind.Array:
                CheckArray(value.AsArray(), schema, path, at, errors, depth);
                break;
            case JsonValueKind.Object:
                CheckObject(value.AsObject(), schema, path, errors, depth);
                break;
        }
    }

    private void CheckOneOf(JsonNode? value, JsonObject schema, JsonArray members, string path, string at, List<string> errors, int depth)
    {
        if (schema["discriminator"]?["propertyName"]?.GetValue<string>() is { } property)
        {
            if (value is not JsonObject obj)
            {
                return; // the type check reports it
            }

            if (obj[property]?.GetValueKind() != JsonValueKind.String)
            {
                errors.Add($"{Join(path, property)} is missing; it says which of {string.Join(", ", members.Select(m => OpenApiSpec.RefName(m)))} this is");
                return;
            }

            var kind = obj[property]!.GetValue<string>();
            var member = Member(schema, members, kind);
            if (member is null)
            {
                errors.Add($"{Join(path, property)} is '{kind}', which is not one of {string.Join(", ", Mapping(schema, members).Select(m => $"'{m.Value}'"))}");
                return;
            }

            Check(value, member, path, errors, depth + 1);
            return;
        }

        var matches = members.Count(m => Validate(value, m!, path).Count == 0);
        if (matches != 1)
        {
            errors.Add($"{at} matches {matches} of oneOf; it must match exactly one");
        }
    }

    /// <summary>The discriminator values and their member schemas: the explicit mapping, then each member's own name.</summary>
    public static List<(string Value, JsonNode Member)> Mapping(JsonObject schema, JsonArray members)
    {
        var explicitMapping = schema["discriminator"]?["mapping"]?.AsObject();
        var result = new List<(string, JsonNode)>();
        foreach (var member in members)
        {
            var reference = member?["$ref"]?.GetValue<string>();
            var value = explicitMapping?.FirstOrDefault(m => m.Value?.GetValue<string>() == reference).Key ?? OpenApiSpec.RefName(member) ?? string.Empty;
            result.Add((value, member!));
        }

        return result;
    }

    private static JsonNode? Member(JsonObject schema, JsonArray members, string kind)
        => Mapping(schema, members).Where(m => m.Value == kind).Select(m => m.Member).FirstOrDefault();

    private static void CheckString(string text, JsonObject schema, string at, List<string> errors)
    {
        var length = text.EnumerateRunes().Count();
        if (Count(schema, "minLength") is { } min && length < min)
        {
            errors.Add($"{at} is {length} characters; the schema's minLength is {min}");
        }

        if (Count(schema, "maxLength") is { } max && length > max)
        {
            errors.Add($"{at} is {length} characters; the schema's maxLength is {max}");
        }

        if (schema["pattern"]?.GetValue<string>() is { } pattern && !Pattern(pattern).IsMatch(text))
        {
            errors.Add($"{at} is '{text}', which doesn't match the pattern {pattern}");
        }

        if (schema["format"]?.GetValue<string>() is { } format && !FormatMatches(text, format))
        {
            errors.Add($"{at} is '{text}', which is not a valid {format}");
        }
    }

    private static void CheckNumber(decimal number, JsonObject schema, string at, List<string> errors)
    {
        if (Number(schema, "minimum") is { } min && number < min)
        {
            errors.Add($"{at} is {number.ToString(CultureInfo.InvariantCulture)}; the schema's minimum is {min.ToString(CultureInfo.InvariantCulture)}");
        }

        if (Number(schema, "maximum") is { } max && number > max)
        {
            errors.Add($"{at} is {number.ToString(CultureInfo.InvariantCulture)}; the schema's maximum is {max.ToString(CultureInfo.InvariantCulture)}");
        }

        if (ExclusiveBound(schema, "exclusiveMinimum", "minimum") is { } xmin && number <= xmin)
        {
            errors.Add($"{at} is {number.ToString(CultureInfo.InvariantCulture)}; it must be greater than {xmin.ToString(CultureInfo.InvariantCulture)}");
        }

        if (ExclusiveBound(schema, "exclusiveMaximum", "maximum") is { } xmax && number >= xmax)
        {
            errors.Add($"{at} is {number.ToString(CultureInfo.InvariantCulture)}; it must be less than {xmax.ToString(CultureInfo.InvariantCulture)}");
        }

        if (Number(schema, "multipleOf") is { } step && step != 0 && number % step != 0)
        {
            errors.Add($"{at} is {number.ToString(CultureInfo.InvariantCulture)}, which is not a multiple of {step.ToString(CultureInfo.InvariantCulture)}");
        }

        var format = schema["format"]?.GetValue<string>();
        if (format == "int32" && (number < int.MinValue || number > int.MaxValue) || format == "int64" && (number < long.MinValue || number > long.MaxValue))
        {
            errors.Add($"{at} is {number.ToString(CultureInfo.InvariantCulture)}, which doesn't fit {format}");
        }
    }

    private void CheckArray(JsonArray array, JsonObject schema, string path, string at, List<string> errors, int depth)
    {
        if (Count(schema, "minItems") is { } min && array.Count < min)
        {
            errors.Add($"{at} has {array.Count} items; the schema's minItems is {min}");
        }

        if (Count(schema, "maxItems") is { } max && array.Count > max)
        {
            errors.Add($"{at} has {array.Count} items; the schema's maxItems is {max}");
        }

        if (schema["uniqueItems"]?.GetValue<bool>() == true
            && array.Select(i => i?.ToJsonString()).Distinct(StringComparer.Ordinal).Count() != array.Count)
        {
            errors.Add($"{at} has duplicate items; the schema says they are unique");
        }

        if (schema["items"] is { } items)
        {
            for (var i = 0; i < array.Count; i++)
            {
                Check(array[i], items, $"{path}[{i}]", errors, depth + 1);
            }
        }
    }

    private void CheckObject(JsonObject obj, JsonObject schema, string path, List<string> errors, int depth)
    {
        foreach (var required in schema["required"] as JsonArray ?? [])
        {
            var name = required!.GetValue<string>();
            if (!obj.ContainsKey(name))
            {
                errors.Add($"{Join(path, name)} is missing; the schema requires it");
            }
        }

        var properties = schema["properties"]?.AsObject();
        foreach (var (name, value) in obj)
        {
            if (properties?[name] is { } property)
            {
                Check(value, property, Join(path, name), errors, depth + 1);
            }
            else if (schema["additionalProperties"] is { } additional)
            {
                if (additional.GetValueKind() == JsonValueKind.False)
                {
                    errors.Add($"{Join(path, name)} is not in the schema, which allows no other properties");
                }
                else if (additional is JsonObject)
                {
                    Check(value, additional, Join(path, name), errors, depth + 1);
                }
            }
        }
    }

    /// <summary>The schema's types (3.1 arrays, or 3.0 nullable), checked against the value.</summary>
    private static bool TypeMatches(JsonNode? value, JsonObject schema, out string expected)
    {
        var types = Types(schema);
        expected = string.Join(" or ", types);
        if (types.Count == 0)
        {
            return true;
        }

        var kind = value?.GetValueKind() ?? JsonValueKind.Null;
        return types.Any(t => t switch
        {
            "null" => kind == JsonValueKind.Null,
            "boolean" => kind is JsonValueKind.True or JsonValueKind.False,
            "string" => kind == JsonValueKind.String,
            "object" => kind == JsonValueKind.Object,
            "array" => kind == JsonValueKind.Array,
            "number" => kind == JsonValueKind.Number,
            "integer" => kind == JsonValueKind.Number && value!.GetValue<decimal>() % 1 == 0,
            _ => true,
        });
    }

    public static List<string> Types(JsonObject schema)
    {
        var types = schema["type"] switch
        {
            JsonArray array => array.Select(t => t!.GetValue<string>()).ToList(),
            JsonValue single => [single.GetValue<string>()],
            _ => [],
        };
        if (types.Count > 0 && schema["nullable"]?.GetValue<bool>() == true && !types.Contains("null"))
        {
            types.Add("null");
        }

        return types;
    }

    private static string Describe(JsonNode? value) => value?.GetValueKind() switch
    {
        null or JsonValueKind.Null => "null",
        JsonValueKind.String => $"the string {value.ToJsonString()}",
        JsonValueKind.Number => $"the number {value.ToJsonString()}",
        JsonValueKind.True or JsonValueKind.False => $"the boolean {value.ToJsonString()}",
        JsonValueKind.Array => "an array",
        _ => "an object",
    };

    public static bool FormatMatches(string text, string format) => format switch
    {
        "date-time" => Regex.IsMatch(text, @"^\d{4}-\d{2}-\d{2}[Tt]\d{2}:\d{2}:\d{2}(\.\d+)?([Zz]|[+-]\d{2}:\d{2})$")
                       && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
        "date" => DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
        "uuid" => Guid.TryParseExact(text, "D", out _),
        "email" => Regex.IsMatch(text, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"),
        "uri" => Uri.TryCreate(text, UriKind.Absolute, out _),
        _ => true,
    };

    public static Regex Pattern(string pattern) => Patterns.GetOrAdd(pattern, p => new Regex(p, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));

    public static string Join(string path, string name) => path.Length == 0 ? name : $"{path}.{name}";

    public static int? Count(JsonObject schema, string keyword) => schema[keyword] is JsonValue v && v.TryGetValue<decimal>(out var d) ? (int)d : null;

    public static decimal? Number(JsonObject schema, string keyword) => schema[keyword] is JsonValue v && v.TryGetValue<decimal>(out var d) ? d : null;

    /// <summary>3.1's numeric exclusiveMinimum, or 3.0's boolean modifier on minimum.</summary>
    public static decimal? ExclusiveBound(JsonObject schema, string exclusive, string inclusive)
        => schema[exclusive] switch
        {
            JsonValue v when v.GetValueKind() == JsonValueKind.True => Number(schema, inclusive),
            JsonValue v when v.TryGetValue<decimal>(out var d) => d,
            _ => null,
        };
}
