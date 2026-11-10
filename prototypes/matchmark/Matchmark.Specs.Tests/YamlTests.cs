using System.Text.Json.Nodes;
using AwesomeAssertions;
using NUnit.Framework;

namespace Matchmark.Specs.Tests;

/// <summary>
/// Matchmark's own YAML reading (Documents.Load) against Microsoft.OpenApi's, the one the readers used before (Q56), over
/// every YAML file vendored in specs/: the same JSON tree, or the difference named. Two differences are Microsoft.OpenApi's
/// and kept as YAML 1.2 says: a null it writes as a sentinel string, and an empty value (<c>components:</c>) it reads as
/// an empty string, which YAML reads as null.
/// </summary>
[TestFixture]
public sealed class YamlTests
{
    public static IEnumerable<string> Yaml() => Specs.Files(".", ".yaml", ".yml");

    [TestCaseSource(nameof(Yaml))]
    public void Matchmark_reads_YAML_as_Microsoft_OpenApi_does(string relative)
    {
        var text = File.ReadAllText(Specs.PathOf(relative));
        var stream = new SharpYaml.Serialization.YamlStream();
        stream.Load(new StringReader(text));
        var theirs = Microsoft.OpenApi.YamlReader.YamlConverter.ToJsonNode(stream.Documents[0]);

        var ours = Documents.Load(text, relative);

        Difference(Normal(ours), Normal(theirs), string.Empty).Should().BeNull(relative);
    }

    [Test]
    public void Every_vendored_YAML_file_is_examined() => Yaml().Should().HaveCountGreaterThan(200);

    /// <summary>
    /// The tree reparsed from its text, so a number held as a long and one held as a decimal compare by value. Microsoft.OpenApi
    /// writes a YAML null as a sentinel string ("openapi-json-null-sentinel-value-..."); it's read back as the null it stands for.
    /// </summary>
    private static JsonNode? Normal(JsonNode? node)
    {
        var text = node is null ? "null" : System.Text.RegularExpressions.Regex.Replace(node.ToJsonString(), "\"openapi-json-null-sentinel-value-[0-9A-F-]+\"", "null");
        return JsonNode.Parse(text);
    }

    /// <summary>The first place two trees differ, as a JSON pointer and both values, or null when they don't.</summary>
    private static string? Difference(JsonNode? ours, JsonNode? theirs, string pointer)
    {
        switch (ours, theirs)
        {
            case (JsonObject a, JsonObject b):
                foreach (var key in a.Select(p => p.Key).Union(b.Select(p => p.Key)))
                {
                    if (Difference(a[key], b[key], $"{pointer}/{key}") is { } found)
                    {
                        return found;
                    }
                }

                return a.Count == b.Count ? null : $"{pointer}: {a.Count} fields, theirs {b.Count}";
            case (JsonArray a, JsonArray b):
                for (var i = 0; i < Math.Max(a.Count, b.Count); i++)
                {
                    if (Difference(i < a.Count ? a[i] : null, i < b.Count ? b[i] : null, $"{pointer}/{i}") is { } found)
                    {
                        return found;
                    }
                }

                return null;
            case (null, JsonValue empty) when empty.GetValueKind() == System.Text.Json.JsonValueKind.String && empty.GetValue<string>().Length == 0:
                return null; // An empty value: null in YAML 1.2, an empty string to Microsoft.OpenApi.
            default:
                return JsonNode.DeepEquals(ours, theirs) ? null : $"{pointer}: ours {ours?.ToJsonString() ?? "null"} ({ours?.GetValueKind()}), theirs {theirs?.ToJsonString() ?? "null"} ({theirs?.GetValueKind()})";
        }
    }
}
