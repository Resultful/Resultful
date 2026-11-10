using System.Text.Json;
using System.Text.Json.Nodes;

namespace Matchmark.Specs.Tests;

/// <summary>
/// Every small way to break a valid document, for comparing a reader with the specification's schema: each field removed,
/// each value replaced by one of another type, an unknown field added to each object, and each array emptied or given a
/// repeat of its first item. Each mutation is the whole document, labelled by where and how it changed.
/// </summary>
internal static class Mutations
{
    public static IEnumerable<(string Label, JsonNode Document)> Of(JsonNode document)
    {
        foreach (var (pointer, node) in Nodes(document, string.Empty))
        {
            if (node is JsonObject obj)
            {
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    yield return ($"{pointer}/{key} removed", With(document, pointer, n => ((JsonObject)n!).Remove(key)));
                }

                yield return ($"{pointer} with an unknown field", With(document, pointer, n => ((JsonObject)n!)["invalidField"] = 1));
            }

            if (node is JsonArray { Count: > 0 })
            {
                yield return ($"{pointer} emptied", With(document, pointer, n => ((JsonArray)n!).Clear()));
                yield return ($"{pointer} with its first item repeated", With(document, pointer, n => ((JsonArray)n!).Add(((JsonArray)n)[0]?.DeepClone())));
            }

            if (pointer.Length > 0)
            {
                var other = node switch
                {
                    JsonValue v when v.GetValueKind() == JsonValueKind.String => (JsonNode)12345,
                    _ => "a string",
                };
                yield return ($"{pointer} as {other.GetValueKind()}", Replaced(document, pointer, other));
            }
        }
    }

    private static IEnumerable<(string Pointer, JsonNode? Node)> Nodes(JsonNode? node, string pointer)
    {
        yield return (pointer, node);
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj)
                {
                    foreach (var child in Nodes(value, $"{pointer}/{Pointer.Escape(key)}"))
                    {
                        yield return child;
                    }
                }

                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    foreach (var child in Nodes(array[i], $"{pointer}/{i}"))
                    {
                        yield return child;
                    }
                }

                break;
        }
    }

    private static JsonNode? At(JsonNode root, string pointer)
    {
        var node = root;
        foreach (var token in pointer.Split('/').Skip(1).Select(Pointer.Unescape))
        {
            node = node is JsonArray array ? array[int.Parse(token, System.Globalization.CultureInfo.InvariantCulture)] : node![token];
        }

        return node;
    }

    private static JsonNode With(JsonNode document, string pointer, Action<JsonNode?> change)
    {
        var copy = document.DeepClone();
        change(At(copy, pointer));
        return copy;
    }

    private static JsonNode Replaced(JsonNode document, string pointer, JsonNode value)
    {
        var copy = document.DeepClone();
        var parentPointer = pointer[..pointer.LastIndexOf('/')];
        var token = Pointer.Unescape(pointer[(pointer.LastIndexOf('/') + 1)..]);
        switch (At(copy, parentPointer))
        {
            case JsonObject parent:
                parent[token] = value;
                break;
            case JsonArray array:
                array[int.Parse(token, System.Globalization.CultureInfo.InvariantCulture)] = value;
                break;
        }

        return copy;
    }

    /// <summary>Where a reader and a schema disagree over a document's mutations: each one, labelled with both verdicts.</summary>
    public static List<string> Disagreements(JsonNode document, Func<JsonNode, IReadOnlyList<Problem>> read, Json.Schema.JsonSchema schema)
    {
        var options = new Json.Schema.EvaluationOptions { OutputFormat = Json.Schema.OutputFormat.Flag };
        var disagreements = new List<string>();
        foreach (var (label, mutated) in Of(document))
        {
            var problems = read(mutated);
            var schemaValid = schema.Evaluate(mutated, options).IsValid;
            if ((problems.Count == 0) != schemaValid)
            {
                disagreements.Add($"{label}: the schema says {(schemaValid ? "valid" : "invalid")}, the reader {(problems.Count == 0 ? "valid" : string.Join("; ", problems))}");
            }
        }

        return disagreements;
    }
}
