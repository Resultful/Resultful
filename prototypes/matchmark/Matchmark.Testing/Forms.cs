using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Matchmark.Testing;

/// <summary>A request body as JSON, whatever it was sent as, and JSON written back the way the request sends it.</summary>
internal sealed partial class RegressionRunner
{
    private JsonNode? RequestJson(RegressionRequest request, SpecOperation? operation)
    {
        if (request.Body is not { } body)
        {
            return null;
        }

        if (Forms.IsForm(request.ContentType) && operation?.Body is { } schema)
        {
            return Forms.Decode(body, schema.Schema, suite.Spec);
        }

        try
        {
            return JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? RequestText(RegressionRequest request, JsonNode? json)
        => Forms.IsForm(request.ContentType) ? Forms.Encode(json) : json?.ToJsonString();
}

/// <summary>
/// Request bodies sent as forms (application/x-www-form-urlencoded), nested values in brackets as Stripe sends them
/// (card[number], items[0][price]). A form carries only text, so reading one back is directed by the schema.
/// </summary>
public static class Forms
{
    public const string MediaType = "application/x-www-form-urlencoded";

    public static bool IsForm(string? mediaType) => string.Equals(mediaType?.Split(';')[0].Trim(), MediaType, StringComparison.OrdinalIgnoreCase);

    /// <summary>A JSON value as form fields: objects and arrays in brackets, nulls left out.</summary>
    public static string Encode(JsonNode? node)
        => string.Join("&", Fields(node, null).Select(f => $"{Uri.EscapeDataString(f.Key)}={Uri.EscapeDataString(f.Value)}"));

    private static IEnumerable<KeyValuePair<string, string>> Fields(JsonNode? node, string? prefix)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj)
                {
                    foreach (var field in Fields(value, prefix is null ? key : $"{prefix}[{key}]"))
                    {
                        yield return field;
                    }
                }

                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    foreach (var field in Fields(array[i], $"{prefix}[{i.ToString(CultureInfo.InvariantCulture)}]"))
                    {
                        yield return field;
                    }
                }

                break;
            case JsonValue value:
                yield return new(prefix ?? string.Empty, value.GetValueKind() switch
                {
                    JsonValueKind.String => value.GetValue<string>(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => value.ToJsonString(),
                });
                break;
        }
    }

    /// <summary>A form read back as the schema would: numbers and true or false from their text, the rest as strings.</summary>
    public static JsonNode Decode(string form, JsonNode schema, OpenApiSpec spec)
    {
        var root = new JsonObject();
        foreach (var pair in form.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=', StringComparison.Ordinal);
            var key = Uri.UnescapeDataString((equals < 0 ? pair : pair[..equals]).Replace('+', ' '));
            var value = equals < 0 ? string.Empty : Uri.UnescapeDataString(pair[(equals + 1)..].Replace('+', ' '));
            var keys = new List<string> { key.IndexOf('[', StringComparison.Ordinal) is var open and >= 0 ? key[..open] : key };
            for (var at = key.IndexOf('[', StringComparison.Ordinal); at >= 0 && key.IndexOf(']', at) is var close and > 0; at = key.IndexOf('[', close))
            {
                keys.Add(key[(at + 1)..close]);
            }

            var node = root;
            for (var i = 0; i < keys.Count; i++)
            {
                var name = keys[i].Length == 0 ? node.Count.ToString(CultureInfo.InvariantCulture) : keys[i];
                if (i == keys.Count - 1)
                {
                    node[name] = value;
                }
                else
                {
                    if (node[name] is not JsonObject next)
                    {
                        next = [];
                        node[name] = next;
                    }

                    node = next;
                }
            }
        }

        return Coerce(root, schema, spec, 0) ?? root;
    }

    private static JsonNode? Coerce(JsonNode? node, JsonNode schemaNode, OpenApiSpec spec, int depth)
    {
        var schema = spec.Resolve(schemaNode);
        var types = SchemaValidator.Types(schema);
        if (depth > 16)
        {
            return node?.DeepClone();
        }

        switch (node)
        {
            case JsonObject obj when types.Contains("array") && schema["items"] is { } items:
                var index = 0;
                return obj.All(p => p.Key == (index++).ToString(CultureInfo.InvariantCulture))
                    ? new JsonArray(obj.Select(p => Coerce(p.Value, items, spec, depth + 1)).ToArray())
                    : obj.DeepClone();
            case JsonObject obj:
                var copy = new JsonObject();
                foreach (var (key, value) in obj)
                {
                    var property = schema["properties"]?[key] ?? (schema["additionalProperties"] is JsonObject additional ? additional : null);
                    copy[key] = property is null ? value?.DeepClone() : Coerce(value, property, spec, depth + 1);
                }

                return copy;
            case JsonValue value when value.GetValueKind() == JsonValueKind.String:
                var text = value.GetValue<string>();
                if ((types.Contains("integer") || types.Contains("number"))
                    && decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                {
                    return JsonValue.Create(number);
                }

                if (types.Contains("boolean") && text is "true" or "false")
                {
                    return JsonValue.Create(text == "true");
                }

                return value.DeepClone();
            default:
                return node?.DeepClone();
        }
    }
}
