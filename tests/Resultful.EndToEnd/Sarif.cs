using System.Text.Json;
using System.Text.Json.Nodes;

namespace Resultful.EndToEnd;

internal static class Sarif
{
    // The rule IDs of every error-level result in a SARIF 2.1 log (warnings as errors are written as errors).
    public static IReadOnlySet<string> ErrorIds(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var ids = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var run in doc.RootElement.GetProperty("runs").EnumerateArray())
        {
            if (!run.TryGetProperty("results", out var results)) continue;
            foreach (var r in results.EnumerateArray())
            {
                var level = r.TryGetProperty("level", out var l) ? l.GetString() : "warning";
                if (level == "error") ids.Add(r.GetProperty("ruleId").GetString()!);
            }
        }

        return ids;
    }

    // Both ways: every expected ID is reported, and nothing else is.
    public static string? Compare(IReadOnlySet<string> expected, IReadOnlySet<string> actual)
    {
        var missing = expected.Except(actual).ToList();
        var extra = actual.Except(expected).ToList();
        if (missing.Count == 0 && extra.Count == 0) return null;
        return $"expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}]"
            + (missing.Count > 0 ? $"; missing {string.Join(", ", missing)}" : "")
            + (extra.Count > 0 ? $"; unexpected {string.Join(", ", extra)}" : "");
    }

    public static IReadOnlySet<string> ReadExpected(string path) => File.ReadAllLines(path)
        .Select(l => l.Split('#')[0].Trim())
        .Where(l => l.Length > 0)
        .ToHashSet(StringComparer.Ordinal);
}

internal static class JsonSubset
{
    // Null when every property and array element in `expected` is in `actual` with an equal value.
    public static string? Mismatch(JsonNode? expected, JsonNode? actual, string path = "$")
    {
        switch (expected)
        {
            case null:
                return actual is null ? null : $"{path}: expected null, got {actual.ToJsonString()}";
            case JsonObject e:
                if (actual is not JsonObject a) return $"{path}: expected an object, got {actual?.ToJsonString() ?? "nothing"}";
                foreach (var (key, value) in e)
                {
                    if (!a.TryGetPropertyValue(key, out var av)) return $"{path}.{key}: missing";
                    if (Mismatch(value, av, $"{path}.{key}") is { } m) return m;
                }

                return null;
            case JsonArray e:
                if (actual is not JsonArray arr || arr.Count != e.Count) return $"{path}: expected {e.ToJsonString()}, got {actual?.ToJsonString() ?? "nothing"}";
                for (var i = 0; i < e.Count; i++)
                {
                    if (Mismatch(e[i], arr[i], $"{path}[{i}]") is { } m) return m;
                }

                return null;
            default:
                return JsonNode.DeepEquals(expected, actual) ? null : $"{path}: expected {expected.ToJsonString()}, got {actual?.ToJsonString() ?? "nothing"}";
        }
    }

    // "errors.password" style paths that must not exist.
    public static bool Has(JsonNode? node, string dottedPath)
    {
        foreach (var part in dottedPath.Split('.'))
        {
            if (node is not JsonObject o || !o.TryGetPropertyValue(part, out node)) return false;
        }

        return true;
    }
}
