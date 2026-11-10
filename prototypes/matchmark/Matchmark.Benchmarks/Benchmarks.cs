using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Matchmark.Generator;
using Matchmark.Testing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Matchmark.Benchmarks;

public static class Program
{
    public static void Main(string[] args) => BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}

/// <summary>The specs benchmarked: the Pet Store always, GitHub's and Stripe's when samples/large holds them.</summary>
public static class Specs
{
    public static readonly string Root = Path.GetFullPath(Path.Combine(Here(), ".."));

    public static IEnumerable<string> Names()
        => new[] { "petstore", "github", "stripe" }.Where(n => File.Exists(PathOf(n)));

    public static string PathOf(string name)
        => name == "petstore" ? Path.Combine(Root, "Matchmark.Sample.Contract", "petstore.yaml") : Path.Combine(Root, "samples", "large", name + ".json");

    private static string Here([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}

/// <summary>The generator's stages, each on its own: read, normalise, shape, model, and a whole run.</summary>
[MemoryDiagnoser]
public class GeneratorStages
{
    private string _text = string.Empty;
    private string _path = string.Empty;

    [ParamsSource(nameof(Names))]
    public string Spec { get; set; } = "petstore";

    public static IEnumerable<string> Names() => Specs.Names();

    [GlobalSetup]
    public void Setup()
    {
        _path = Specs.PathOf(Spec);
        _text = File.ReadAllText(_path);
    }

    /// <summary>Microsoft.OpenApi's reading of the document, which every later stage starts from.</summary>
    [Benchmark]
    public object? Read() => SpecReader.Read(_text, _path, out _);

    /// <summary>The normalised JSON the engine reads.</summary>
    [Benchmark]
    public string Normalize() => SpecJson.Normalize(_text, _path);

    /// <summary>The parse step: the equatable ApiShape Verify reads.</summary>
    [Benchmark]
    public object? Shape() => OpenApiSourceGenerator.Parse(new SpecInput(_path, _text, "Api", "Api", "Verify"));

    /// <summary>The read-only model behind the derived evidence.</summary>
    [Benchmark]
    public object? Model() => ApiModel.Read(_text, _path);

    /// <summary>A whole generator run, Contract and Server, as the compiler does it on a cold driver.</summary>
    [Benchmark]
    public int Generate()
    {
        var driver = CSharpGeneratorDriver.Create([new OpenApiSourceGenerator().AsSourceGenerator()], [new Text(_path, _text)], optionsProvider: new Options());
        return driver.RunGenerators(CSharpCompilation.Create("Bench")).GetRunResult().Results[0].GeneratedSources.Length;
    }

    private sealed class Text(string path, string text) : AdditionalText
    {
        public override string Path => path;

        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text);
    }

    private sealed class Options : AnalyzerConfigOptionsProvider
    {
        private static readonly Values File = new(new Dictionary<string, string>
        {
            ["build_metadata.AdditionalFiles.Matchmark"] = "true",
            ["build_metadata.AdditionalFiles.MatchmarkApiName"] = "Api",
            ["build_metadata.AdditionalFiles.MatchmarkNamespace"] = "Api",
            ["build_metadata.AdditionalFiles.MatchmarkParts"] = "Contract,Server",
        });

        public override AnalyzerConfigOptions GlobalOptions { get; } = new Values([]);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => File;

        private sealed class Values(Dictionary<string, string> values) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
        }
    }
}

/// <summary>The engine's stages on normalised JSON: the suite, and sampling and validating every component schema.</summary>
[MemoryDiagnoser]
public class EngineStages
{
    private string _json = string.Empty;
    private OpenApiSpec _spec = null!;
    private List<JsonObject> _references = [];

    [ParamsSource(nameof(Names))]
    public string Spec { get; set; } = "petstore";

    public static IEnumerable<string> Names() => Specs.Names();

    [GlobalSetup]
    public void Setup()
    {
        var path = Specs.PathOf(Spec);
        _json = SpecJson.Normalize(File.ReadAllText(path), path);
        _spec = OpenApiSpec.Parse(_json);
        _references = _spec.Schemas.Select(s => new JsonObject { ["$ref"] = $"#/components/schemas/{s.Key}" }).ToList();
    }

    /// <summary>Every case the spec's regression suite generates.</summary>
    [Benchmark]
    public int Suite() => RegressionSuite.FromJson(_json).Cases.Count;

    /// <summary>A full sample of every component schema, each validated: what the oracle test does before its oracles.</summary>
    [Benchmark]
    public int SampleAndValidate()
    {
        var validator = new SchemaValidator(_spec);
        var samples = new Samples(_spec, validator);
        var valid = 0;
        foreach (var reference in _references)
        {
            if (samples.TrySample(reference, true, Direction.Any, out var value) && validator.IsValid(value, reference))
            {
                valid++;
            }
        }

        return valid;
    }

    /// <summary>The violations of every component schema's sample: one invalid value per constraint.</summary>
    [Benchmark]
    public int Violations()
    {
        var validator = new SchemaValidator(_spec);
        var samples = new Samples(_spec, validator);
        var count = 0;
        foreach (var reference in _references)
        {
            if (samples.TrySample(reference, true, Direction.Any, out var value))
            {
                count += samples.Violations(value, reference).Count();
            }
        }

        return count;
    }
}
