using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Matchmark.Cli;
using NUnit.Framework;

namespace Matchmark.Generator.Tests;

/// <summary>
/// matchmark bundle (question 2, option b): a spec split across files becomes one the generator reads. References are
/// resolved relative to the file that makes them, targets hoisted by where they're used, and a cycle (an owner's pets
/// are pets) ends at the hoisted name.
/// </summary>
[TestFixture]
public sealed class BundleTests
{
    private static readonly string Split = Path.GetFullPath(Path.Combine(Directory(), "..", "samples", "split", "openapi.yaml"));

    [Test]
    public void A_split_spec_becomes_one_with_each_part_a_component()
    {
        var bundled = BundleCommand.Bundle(Split);
        var components = bundled["components"]!;
        components["schemas"]!.AsObject().Select(s => s.Key).Should().BeEquivalentTo("pet", "Owner", "Problem");
        components["parameters"]!["limit"]!["in"]!.GetValue<string>().Should().Be("query");
        components["responses"]!["Problem"]!["content"]!["application/problem+json"]!["schema"]!["$ref"]!.GetValue<string>().Should().Be("#/components/schemas/Problem");
        components["schemas"]!["Owner"]!["properties"]!["pets"]!["items"]!["$ref"]!.GetValue<string>().Should().Be("#/components/schemas/pet", "the cycle ends at the hoisted name");
        bundled.ToJsonString().Should().NotContain(".yaml", "every reference is local now");
    }

    [Test]
    public void The_bundle_generates_code_that_compiles()
    {
        var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "split-bundled.json");
        File.WriteAllText(path, BundleCommand.Bundle(Split).ToJsonString());
        var (_, diagnostics) = GeneratedServer.Build("Split", path, [], string.Empty, withRules: false);
        diagnostics.Should().NotContain(d => d.Id == "MM001" || d.Id == "MM002" || d.Id == "MM003");
    }

    private static string Directory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
