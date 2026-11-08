using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AwesomeAssertions;
using NUnit.Framework;

namespace Matchmark.Generator.Tests;

/// <summary>
/// The generator package declares its own build settings: build/Matchmark.Generator.props makes visible every item
/// metadata name the generator reads, so a project only marks its spec. This keeps the two in step.
/// </summary>
[TestFixture]
public sealed partial class PackagingTests
{
    private static readonly string Generator = Path.GetFullPath(Path.Combine(ThisDirectory(), "..", "Matchmark.Generator"));

    [Test]
    public void The_props_file_makes_visible_every_metadata_name_the_generator_reads()
    {
        var source = File.ReadAllText(Path.Combine(Generator, "OpenApiSourceGenerator.cs"));
        var read = MetadataRead().Matches(source).Select(m => m.Groups[1].Value).Concat(Companion().Matches(source).Select(m => m.Groups[1].Value)).ToHashSet();

        var declared = XDocument.Load(Path.Combine(Generator, "build", "Matchmark.Generator.props")).Descendants("CompilerVisibleItemMetadata")
            .Where(e => (string?)e.Attribute("Include") == "AdditionalFiles")
            .Select(e => (string)e.Attribute("MetadataName")!);

        read.Should().NotBeEmpty();
        declared.Should().BeEquivalentTo(read);
    }

    [Test]
    public void The_package_carries_the_generator_its_reader_and_the_props()
    {
        var project = XDocument.Load(Path.Combine(Generator, "Matchmark.Generator.csproj"));
        var packed = project.Descendants("None").Where(e => (string?)e.Attribute("Pack") == "true")
            .Select(e => $"{Path.GetFileName((string)e.Attribute("Include")!)} -> {(string)e.Attribute("PackagePath")!}");

        packed.Should().BeEquivalentTo(
            "$(OutputPath)$(AssemblyName).dll -> analyzers/dotnet/cs",
            "Microsoft.OpenApi.dll -> analyzers/dotnet/cs",
            "Microsoft.OpenApi.YamlReader.dll -> analyzers/dotnet/cs",
            "SharpYaml.dll -> analyzers/dotnet/cs",
            "Matchmark.Generator.props -> build;buildTransitive");
    }

    [GeneratedRegex(@"MetadataPrefix \+ ""(Matchmark\w*)""")]
    private static partial Regex MetadataRead();

    [GeneratedRegex(@"Companions\(context, ""(Matchmark\w*)""\)")]
    private static partial Regex Companion();

    private static string ThisDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
