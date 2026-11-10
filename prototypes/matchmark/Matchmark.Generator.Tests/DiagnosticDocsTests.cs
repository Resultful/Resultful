using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace Matchmark.Generator.Tests;

/// <summary>
/// The diagnostics reference (docs/reference/diagnostics.md) and the generator's descriptors stay in step: every
/// descriptor is documented with its severity and title, and every documented id is one the generator has. Ids are
/// stable, so a retired one stays in the table, marked.
/// </summary>
[TestFixture]
public sealed partial class DiagnosticDocsTests
{
    private static readonly string Reference = Path.GetFullPath(Path.Combine(Directory(), "..", "docs", "reference", "diagnostics.md"));

    private static IReadOnlyList<DiagnosticDescriptor> Descriptors()
        => typeof(OpenApiSourceGenerator).Assembly.GetTypes()
            .SelectMany(t => t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Where(f => f.FieldType == typeof(DiagnosticDescriptor))
            .Select(f => (DiagnosticDescriptor)f.GetValue(null)!)
            .Where(d => d.Id.StartsWith("MM", StringComparison.Ordinal))
            .DistinctBy(d => d.Id)
            .ToList();

    private static Dictionary<string, (string Severity, string Title, string Line)> Documented()
        => File.ReadAllLines(Reference).Select(l => Row().Match(l)).Where(m => m.Success)
            .ToDictionary(m => m.Groups["id"].Value, m => (m.Groups["severity"].Value.Trim(), m.Groups["title"].Value.Trim(), m.Value), StringComparer.Ordinal);

    [Test]
    public void Every_descriptor_is_documented_with_its_severity_and_title()
    {
        var descriptors = Descriptors();
        var documented = Documented();
        descriptors.Should().HaveCountGreaterThan(25, "the test examined the generator's descriptors");
        var drift = new List<string>();
        foreach (var descriptor in descriptors)
        {
            var title = descriptor.Title.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!documented.TryGetValue(descriptor.Id, out var row))
            {
                drift.Add($"{descriptor.Id} ({title}) is reported but not documented");
            }
            else if (row.Severity != descriptor.DefaultSeverity.ToString() || row.Title != title)
            {
                drift.Add($"{descriptor.Id}: documented as {row.Severity}, '{row.Title}'; reported as {descriptor.DefaultSeverity}, '{title}'");
            }
        }

        drift.Should().BeEmpty();
    }

    [Test]
    public void Every_documented_id_is_one_the_generator_reports_or_is_marked_retired()
    {
        var ids = Descriptors().Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        var documented = Documented();
        documented.Should().HaveCountGreaterThan(25, "the test examined the reference's tables");
        documented.Where(d => !ids.Contains(d.Key) && !d.Value.Line.Contains("retired", StringComparison.OrdinalIgnoreCase))
            .Select(d => d.Key).Should().BeEmpty("a documented id the generator doesn't have must be marked retired");
    }

    [GeneratedRegex(@"^\| (?<id>MM\d{3})[^|]*\| (?<severity>[^|]+)\| (?<title>[^|]+)\|")]
    private static partial Regex Row();

    private static string Directory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
