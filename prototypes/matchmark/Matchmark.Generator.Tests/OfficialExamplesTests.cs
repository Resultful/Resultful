using System.Text.Json.Nodes;
using AwesomeAssertions;
using Matchmark.Testing;
using NUnit.Framework;

namespace Matchmark.Generator.Tests;

/// <summary>
/// Full support, measured (decisions Q20, Q51): the specifications' own examples, fetched at pinned commits by
/// samples/examples/fetch.sh, read, generated and compiled. Each kind writes its line to examples-ledger.txt: the numbers
/// should only rise, and what doesn't pass is named. Runs when MATCHMARK_EXAMPLES names the fetched directory.
/// </summary>
[TestFixture]
public sealed class OfficialExamplesTests
{
    private static readonly string? Root = Environment.GetEnvironmentVariable("MATCHMARK_EXAMPLES");

    private static string Folder(string repository, string path)
    {
        if (Root is null || !Directory.Exists(Path.Combine(Root, repository)))
        {
            Assert.Ignore("Set MATCHMARK_EXAMPLES to a directory fetched by samples/examples/fetch.sh.");
        }

        return Path.Combine(Root!, repository, path);
    }

    private static void Ledger(string kind, int passed, int total, IEnumerable<string> failures)
    {
        var line = $"{kind}: {passed}/{total}" + string.Concat(failures.Select(f => $"\n  {f}"));
        TestContext.Out.WriteLine(line);
        File.AppendAllText(Path.Combine(TestContext.CurrentContext.WorkDirectory, "examples-ledger.txt"), line + Environment.NewLine);
    }

    /// <summary>OpenAPI 2.0 to 3.2 (learn.openapis.org): each read, then generated as a contract and server that compile.</summary>
    [TestCase("v2.0/yaml")]
    [TestCase("v3.0")]
    [TestCase("v3.1")]
    [TestCase("v3.2")]
    public void OpenApi_examples_are_read_and_generate_code_that_compiles(string version)
    {
        var folder = Folder("OAI_learn.openapis.org", Path.Combine("examples", version));
        // One file per example: the YAML, and only the root of a split one.
        var specs = Directory.GetFiles(folder, "*.yaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}petstore-separate{Path.DirectorySeparatorChar}", StringComparison.Ordinal) || f.EndsWith("swagger.yaml", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).ToList();
        var failures = new List<string>();
        foreach (var spec in specs)
        {
            var name = Path.GetRelativePath(folder, spec);
            var document = SpecReader.Read(File.ReadAllText(spec), spec, out var problems);
            if (document is null)
            {
                failures.Add($"{name}: not read: {string.Join("; ", problems)}");
                continue;
            }

            try
            {
                var (_, diagnostics) = GeneratedServer.Build("Example", spec, [], string.Empty, withRules: false);
                if (diagnostics.FirstOrDefault(d => d.Id == "CS8785") is { } crashed)
                {
                    failures.Add($"{name}: the generator failed: {crashed.GetMessage(null)}");
                }
            }
            catch (AssertionException e)
            {
                failures.Add($"{name}: doesn't compile: {e.Message.Split('\n')[0]}");
            }
        }

        Ledger($"OpenAPI {version}", specs.Count - failures.Count, specs.Count, failures);
        specs.Should().NotBeEmpty();
    }

    /// <summary>AsyncAPI 3.0 and 3.1 (asyncapi/spec): each read (not-asyncapi.yml shows the schema keyword not; it's valid).</summary>
    [Test]
    public void AsyncApi_examples_are_read()
    {
        var folder = Folder("asyncapi_spec", "examples");
        var specs = Directory.GetFiles(folder, "*.y*ml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}common{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).ToList();
        var failures = new List<string>();
        foreach (var spec in specs)
        {
            var name = Path.GetRelativePath(folder, spec);
            try
            {
                AsyncApiDocument.Parse(SpecJson.AsyncApi(File.ReadAllText(spec), spec));
            }
            catch (Exception e) when (e is InvalidOperationException or ArgumentException or System.Text.Json.JsonException or FormatException or KeyNotFoundException)
            {
                failures.Add($"{name}: {e.Message.Split('\n')[0]}");
            }
        }

        Ledger("AsyncAPI", specs.Count - failures.Count, specs.Count, failures);
        specs.Should().NotBeEmpty();
    }

    /// <summary>Arazzo 1.0 and 1.1 (OAI/Arazzo-Specification): each workflow document read.</summary>
    [Test]
    public void Arazzo_examples_are_read()
    {
        var folder = Folder("OAI_Arazzo-Specification", "examples");
        var documents = Directory.GetFiles(folder, "*.yaml", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains("arazzo:", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToList();
        var failures = new List<string>();
        foreach (var document in documents)
        {
            try
            {
                var arazzo = ArazzoDocument.Parse(SpecJson.Arazzo(File.ReadAllText(document), document));
                if (arazzo.Workflows.Count == 0)
                {
                    failures.Add($"{Path.GetRelativePath(folder, document)}: no workflows read");
                }
            }
            catch (Exception e) when (e is InvalidOperationException or ArgumentException or System.Text.Json.JsonException or FormatException or KeyNotFoundException)
            {
                failures.Add($"{Path.GetRelativePath(folder, document)}: {e.Message.Split('\n')[0]}");
            }
        }

        Ledger("Arazzo", documents.Count - failures.Count, documents.Count, failures);
        documents.Should().NotBeEmpty();
    }

    /// <summary>Overlay (OAI/Overlay-Specification's compliant sets): each overlay applied gives the set's output exactly, as JSON.</summary>
    [Test]
    public void Overlay_compliant_sets_give_their_outputs()
    {
        var folder = Folder("OAI_Overlay-Specification", "compliant-sets");
        var sets = Directory.GetDirectories(folder).Where(d => File.Exists(Path.Combine(d, "overlay.yaml"))).Order(StringComparer.Ordinal).ToList();
        var failures = new List<string>();
        foreach (var set in sets)
        {
            var name = Path.GetFileName(set);
            try
            {
                var input = Path.Combine(set, "openapi.yaml");
                var applied = JsonNode.Parse(Overlay.Apply(File.ReadAllText(input), input, [(File.ReadAllText(Path.Combine(set, "overlay.yaml")), Path.Combine(set, "overlay.yaml"))]));
                var output = Path.Combine(set, "output.yaml");
                var expected = JsonNode.Parse(Overlay.Apply(File.ReadAllText(output), output, []));
                if (!JsonNode.DeepEquals(applied, expected))
                {
                    failures.Add($"{name}: differs at {RegressionReport.FirstDifference(expected, applied, string.Empty)}");
                }
            }
            catch (Exception e) when (e is InvalidOperationException or ArgumentException or System.Text.Json.JsonException or FormatException)
            {
                failures.Add($"{name}: {e.Message.Split('\n')[0]}");
            }
        }

        Ledger("Overlay compliant sets", sets.Count - failures.Count, sets.Count, failures);
        sets.Should().NotBeEmpty();
    }
}
