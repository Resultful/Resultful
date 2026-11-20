using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Resultful.Text.Json;

namespace Resultful.Testing.Json;

/// <summary>
/// Compares a value's JSON with an expected literal: <c>JsonAssert.Equal("""{"ok":true,"value":1}""", result)</c>.
/// The value is written with <see cref="Options"/> (or the options given) and compared with
/// <see cref="JsonNode.DeepEquals"/>, so member order and whitespace don't matter. A failure prints both, indented, and
/// the path of the first difference.
/// </summary>
public static class JsonAssert
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// The fixed options: <see cref="JsonSerializerDefaults.Web"/> (camelCase leaf properties) with Resultful's
    /// converters. Writing needs no error cases, so none are registered.
    /// </summary>
    [SuppressMessage("AOT", "IL3050", Justification = "A test helper; tests don't run native AOT.")]
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    /// <summary>Asserts that <paramref name="actual"/> is written as <paramref name="expectedJson"/>.</summary>
    /// <exception cref="JsonAssertException">The JSON differs, or <paramref name="expectedJson"/> isn't JSON.</exception>
    [RequiresUnreferencedCode("Serializes with reflection-based metadata.")]
    [RequiresDynamicCode("Serializes with reflection-based metadata.")]
    public static void Equal<T>([StringSyntax(StringSyntaxAttribute.Json)] string expectedJson, T actual) => Equal(expectedJson, actual, Options);

    /// <summary>Asserts that <paramref name="actual"/>, written with <paramref name="options"/>, is <paramref name="expectedJson"/>.</summary>
    /// <remarks>Member order is ignored; use <see cref="EqualOrdered{T}(string, T)"/> to pin it too.</remarks>
    /// <exception cref="JsonAssertException">The JSON differs, or <paramref name="expectedJson"/> isn't JSON.</exception>
    [RequiresUnreferencedCode("Serializes with reflection-based metadata.")]
    [RequiresDynamicCode("Serializes with reflection-based metadata.")]
    public static void Equal<T>([StringSyntax(StringSyntaxAttribute.Json)] string expectedJson, T actual, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Compare(Parse(expectedJson, "expected"), JsonSerializer.Serialize(actual, options), ordered: false);
    }

    /// <summary>
    /// Like <see cref="Equal{T}(string, T)"/>, but member order matters too: the wire format's order is part of the
    /// contract.
    /// </summary>
    /// <exception cref="JsonAssertException">The JSON or its member order differs, or <paramref name="expectedJson"/> isn't JSON.</exception>
    [RequiresUnreferencedCode("Serializes with reflection-based metadata.")]
    [RequiresDynamicCode("Serializes with reflection-based metadata.")]
    public static void EqualOrdered<T>([StringSyntax(StringSyntaxAttribute.Json)] string expectedJson, T actual) =>
        Compare(Parse(expectedJson, "expected"), JsonSerializer.Serialize(actual, Options), ordered: true);

    /// <summary>Asserts that two JSON texts are equal, member order and whitespace aside.</summary>
    /// <exception cref="JsonAssertException">They differ, or either isn't JSON.</exception>
    public static void EqualJson([StringSyntax(StringSyntaxAttribute.Json)] string expectedJson, [StringSyntax(StringSyntaxAttribute.Json)] string actualJson)
    {
        var expected = Parse(expectedJson, "expected");
        Parse(actualJson, "actual");
        Compare(expected, actualJson, ordered: false);
    }

    private static JsonNode? Parse(string json, string which)
    {
        ArgumentNullException.ThrowIfNull(json, which + "Json");
        try
        {
            return JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new JsonAssertException($"The {which} JSON isn't valid: " + ex.Message + Environment.NewLine + json, ex);
        }
    }

    private static void Compare(JsonNode? expected, string actualJson, bool ordered)
    {
        var actual = JsonNode.Parse(actualJson);
        var where = FirstDifference(expected, actual, "$", ordered);
        if (where is null) return;

        var message = new StringBuilder()
            .AppendLine((JsonNode.DeepEquals(expected, actual) ? "JSON member order differs at " : "JSON differs at ") + where + ".")
            .AppendLine("Expected:")
            .AppendLine(Show(expected))
            .AppendLine("Actual:")
            .AppendLine(Show(actual))
            .Append("Actual, as written: ").Append(actualJson);
        throw new JsonAssertException(message.ToString());
    }

    [SuppressMessage("AOT", "IL3050", Justification = "A test helper; tests don't run native AOT.")]
    private static JsonSerializerOptions CreateOptions() => new JsonSerializerOptions(JsonSerializerDefaults.Web).AddResultful();

    private static string Show(JsonNode? node) => node is null ? "null" : node.ToJsonString(Indented);

    /// <summary>The path of the first difference, with what each side has there; null when equal.</summary>
    internal static string? FirstDifference(JsonNode? expected, JsonNode? actual, string path, bool ordered)
    {
        switch (expected, actual)
        {
            case (JsonObject e, JsonObject a):
                if (ordered)
                {
                    var keys = e.Select(p => p.Key).ToList();
                    var actualKeys = a.Select(p => p.Key).ToList();
                    for (var i = 0; i < Math.Min(keys.Count, actualKeys.Count); i++)
                    {
                        if (keys[i] != actualKeys[i]) return $"{path}: member {i.ToString(CultureInfo.InvariantCulture)} expected \"{keys[i]}\", actual \"{actualKeys[i]}\"";
                    }
                }

                foreach (var (key, value) in e)
                {
                    if (!a.TryGetPropertyValue(key, out var other)) return $"{path}.{key} (missing from actual)";
                    if (FirstDifference(value, other, path + "." + key, ordered) is { } inner) return inner;
                }

                foreach (var (key, _) in a)
                {
                    if (!e.ContainsKey(key)) return $"{path}.{key} (not expected)";
                }

                return null;
            case (JsonArray e, JsonArray a):
                for (var i = 0; i < Math.Min(e.Count, a.Count); i++)
                {
                    if (FirstDifference(e[i], a[i], path + "[" + i.ToString(CultureInfo.InvariantCulture) + "]", ordered) is { } inner) return inner;
                }

                return e.Count == a.Count ? null : $"{path} (expected {e.Count} items, actual {a.Count})";
            default:
                return JsonNode.DeepEquals(expected, actual) ? null : $"{path}: expected {Show(expected)}, actual {Show(actual)}";
        }
    }
}

/// <summary>Thrown by <see cref="JsonAssert"/> when the JSON differs.</summary>
public sealed class JsonAssertException : Exception
{
    /// <summary>Creates the exception.</summary>
    public JsonAssertException()
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    public JsonAssertException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with a message and its cause.</summary>
    public JsonAssertException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
