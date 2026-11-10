using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;
using Matchmark.Specs;
using Matchmark.Specs.Arazzo;
using Matchmark.Specs.AsyncApi;
using Matchmark.Specs.Overlay;

namespace Matchmark.Benchmarks;

/// <summary>
/// Matchmark's own readers (Q56) over every document vendored in specs/, per format: loading the YAML, reading it, and,
/// for comparison, the official schema judging the same documents (the regression suite's oracle).
/// </summary>
[MemoryDiagnoser]
public class ReaderStages
{
    private readonly List<(string Text, string Path)> _texts = [];
    private readonly List<JsonNode> _documents = [];
    private Json.Schema.JsonSchema? _schema;
    private List<(JsonNode Input, OverlayDocument Overlay)> _sets = [];

    [Params("overlay", "arazzo", "asyncapi")]
    public string Format { get; set; } = "overlay";

    private static string Specs => Path.Combine(Benchmarks.Specs.Root, "specs");

    [GlobalSetup]
    public void Setup()
    {
        var folder = Format switch { "overlay" => "overlay/tests", "arazzo" => "arazzo", _ => "asyncapi/examples" };
        foreach (var file in Directory.GetFiles(Path.Combine(Specs, folder), "*", SearchOption.AllDirectories)
                     .Where(f => f.EndsWith(".yaml", StringComparison.Ordinal) || f.EndsWith(".yml", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            var text = File.ReadAllText(file);
            var node = Documents.Load(text, file);
            var key = Format == "asyncapi" ? "asyncapi" : Format;
            if (node is JsonObject obj && obj[key] is not null && (Format != "overlay" || file.Contains("/pass/", StringComparison.Ordinal)))
            {
                _texts.Add((text, file));
                _documents.Add(node);
            }
        }

        var schema = Format switch
        {
            "overlay" => Documents.Load(File.ReadAllText(Path.Combine(Specs, "overlay/schemas/v1.1/schema.yaml")), "schema.yaml"),
            "arazzo" => Documents.Load(File.ReadAllText(Path.Combine(Specs, "arazzo/schemas/1.1.yaml")), "schema.yaml"),
            _ => Documents.Load(File.ReadAllText(Path.Combine(Specs, "asyncapi/schemas/3.0.0.json")), "schema.json"),
        };
        if (Format == "asyncapi")
        {
            schema["definitions"]!["avroSchema_v1"] = new JsonObject { ["not"] = new JsonObject { ["required"] = new JsonArray("$ref") } };
        }

        _schema = Json.Schema.JsonSchema.FromText(schema.ToJsonString());
        _sets = Format != "overlay"
            ? []
            : Directory.GetDirectories(Path.Combine(Specs, "overlay/compliant-sets")).Select(d => (
                Documents.Load(File.ReadAllText(Path.Combine(d, "openapi.yaml")), "openapi.yaml"),
                OverlayReader.Read(File.ReadAllText(Path.Combine(d, "overlay.yaml")), "overlay.yaml").Document!)).ToList();
    }

    /// <summary>Every document's YAML, loaded into a JSON tree (Matchmark.Specs, YAML 1.2's core schema).</summary>
    [Benchmark]
    public int Load() => _texts.Sum(t => Documents.Load(t.Text, t.Path) is JsonObject o ? o.Count : 0);

    /// <summary>Every loaded document, read by the format's reader.</summary>
    [Benchmark]
    public int Read() => _documents.Sum(d => Format switch
    {
        "overlay" => OverlayReader.Read(d).Problems.Count,
        "arazzo" => ArazzoReader.Read(d).Problems.Count,
        _ => AsyncApiReader.Read(d).Problems.Count,
    } + 1);

    /// <summary>Every loaded document, judged by the format's official schema (JsonSchema.Net 7.3.3), for comparison.</summary>
    [Benchmark]
    public int OfficialSchema() => _documents.Count(d => _schema!.Evaluate(d, new Json.Schema.EvaluationOptions { OutputFormat = Json.Schema.OutputFormat.Flag }).IsValid);

    /// <summary>Overlay: every compliant set's overlay applied to its input; AsyncAPI: every example forward-ported (a no-op at 3.x).</summary>
    [Benchmark]
    public int Apply()
    {
        if (Format == "overlay")
        {
            foreach (var (input, overlay) in _sets)
            {
                OverlayApplier.Apply(input.DeepClone(), overlay);
            }

            return _sets.Count;
        }

        return Format == "asyncapi" ? _documents.Count(d => AsyncApiUpgrade.Upgrade((JsonObject)d.DeepClone()).Document is not null) : 0;
    }
}
