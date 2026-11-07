using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Matchmark.Testing;
using NUnit.Framework;

namespace Matchmark.Generator.Tests;

/// <summary>
/// The sample corpus, measured: for each spec, what the generator has to skip and what the regression engine builds.
/// The ledger below is the current state. Supporting more of a sample changes it (update it, and say what got better);
/// so does breaking something that worked.
/// </summary>
[TestFixture]
public sealed class CorpusTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(ThisDirectory(), ".."));

    private static readonly (string Name, string Path, string[] Arazzo)[] Samples =
    [
        ("petstore", "Matchmark.Sample.Contract/petstore.yaml", ["Matchmark.Sample.Contract/petstore.arazzo.yaml"]),
        ("museum", "samples/museum/openapi.yaml", ["samples/museum/arazzo/museum-api.arazzo.yaml", "samples/museum/arazzo/museum-tickets.arazzo.yaml"]),
        ("orders", "samples/orders/openapi.yaml", ["samples/orders/orders.arazzo.yaml"]),
    ];

    [Test]
    public void The_ledger_of_what_each_sample_gets()
    {
        var ledger = Samples.Select(sample =>
        {
            var path = Path.Combine(Root, sample.Path);
            var text = File.ReadAllText(path);
            Scaffolder.Scaffold(text, Path.GetFileName(path), "Corpus.Api", "Corpus", "Corpus", controllers: true, out var warnings);
            var arazzo = sample.Arazzo.Select(a => SpecJson.Arazzo(File.ReadAllText(Path.Combine(Root, a)), a)).ToArray();
            var suite = RegressionSuite.FromJson(SpecJson.Normalize(text, path), arazzo);
            var runnable = suite.Arazzo.Count(w => suite.ArazzoGaps(w).Count == 0);
            return $"{sample.Name}: generator skips {warnings.Distinct().Count()}; suite {suite.Cases.Count} cases, {suite.Workflows.Count} workflows from links, "
                   + $"{runnable} of {suite.Arazzo.Count} Arazzo workflows runnable, {suite.Gaps.Count} gaps";
        }).ToList();

        File.WriteAllLines(Path.Combine(TestContext.CurrentContext.WorkDirectory, "corpus-ledger.txt"), ledger);
        ledger.Should().Equal(
            "petstore: generator skips 0; suite 90 cases, 1 workflows from links, 1 of 1 Arazzo workflows runnable, 0 gaps",
            "museum: generator skips 29; suite 100 cases, 0 workflows from links, 3 of 3 Arazzo workflows runnable, 0 gaps",
            "orders: generator skips 3; suite 85 cases, 2 workflows from links, 1 of 1 Arazzo workflows runnable, 0 gaps");
    }

    private static string ThisDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
