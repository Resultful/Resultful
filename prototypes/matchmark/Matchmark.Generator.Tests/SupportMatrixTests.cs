using System.Runtime.CompilerServices;
using System.Text;
using AwesomeAssertions;
using Matchmark.Testing;
using NUnit.Framework;

namespace Matchmark.Generator.Tests;

/// <summary>
/// The support matrix (VISION.md, decision Q29): each format and version in samples/versions, probed for what Matchmark
/// does with it, written to docs/reference/support-matrix.md. The page is never written by hand: when a probe's result
/// changes, this test writes the new page and fails, so the change is committed with the code that made it.
/// </summary>
[TestFixture]
public sealed class SupportMatrixTests
{
    private static readonly string Versions = Path.GetFullPath(Path.Combine(ThisDirectory(), "..", "samples", "versions"));

    private static readonly string Page = Path.GetFullPath(Path.Combine(ThisDirectory(), "..", "docs", "reference", "support-matrix.md"));

    private const string Planned = "planned";

    private const string NotApplicable = "n/a";

    [Test]
    public async Task The_support_matrix_is_the_one_committed()
    {
        var rows = new List<(string Format, string Version, string Read, string Verify, string Host, string Generate, string Write)>();
        foreach (var version in new[] { "2.0", "3.0", "3.1", "3.2" })
        {
            rows.Add(await OpenApi(version));
        }

        foreach (var version in new[] { "2.6", "3.0", "3.1" })
        {
            rows.Add(AsyncApi(version));
        }

        foreach (var version in new[] { "1.0", "1.1" })
        {
            rows.Add(await Arazzo(version));
        }

        foreach (var version in new[] { "1.0", "1.1", "1.2" })
        {
            rows.Add(Overlay(version));
        }

        var page = Render(rows);
        var committed = File.Exists(Page) ? File.ReadAllText(Page).ReplaceLineEndings("\n") : string.Empty;
        if (committed != page)
        {
            File.WriteAllText(Page, page);
        }

        committed.Should().Be(page, "the support matrix changed: the new page has been written to docs/reference/support-matrix.md; commit it");
    }

    /// <summary>Read: the spec is read. Verify: a suite is generated. Host: the suite passes against a twin. Generate: the code compiles with nothing skipped.</summary>
    private static async Task<(string, string, string, string, string, string, string)> OpenApi(string version)
    {
        var path = Path.Combine(Versions, $"openapi-{version}.yaml");
        if (!Probe(() => SpecJson.Normalize(File.ReadAllText(path), path), out var json, out var read))
        {
            return ("OpenAPI", version, read, "", "", "", Write(path));
        }

        var suite = RegressionSuite.FromJson(json!);
        var verify = suite.Cases.Count > 0 ? $"✓ {suite.Cases.Count} cases" : "✗ no cases";
        using var client = new HttpClient(new SpecTwin(json!)) { BaseAddress = new Uri("http://twin/") };
        var report = await suite.RunAsync(client, new RegressionOptions { MaxRetryWait = TimeSpan.Zero });
        var host = report.Failures.Count == 0 ? "✓" : $"✗ {report.Failures.Count} failures";
        return ("OpenAPI", version, read, verify, host, Generate(path, []), Write(path));
    }

    /// <summary>Read: the document is read. Verify: a consumer suite is generated. Generate: typed messages compile.</summary>
    private static (string, string, string, string, string, string, string) AsyncApi(string version)
    {
        var path = Path.Combine(Versions, $"asyncapi-{version}.yaml");
        if (!Probe(() => AsyncApiDocument.Parse(SpecJson.AsyncApi(File.ReadAllText(path), path)), out var document, out var read))
        {
            return ("AsyncAPI", version, read, "", Planned, "", Write(path));
        }

        var suite = document!.ConsumerSuite();
        var verify = suite is { Cases.Count: > 0 } ? $"✓ {suite.Cases.Count} cases" : "✗ no consumer suite";
        return ("AsyncAPI", version, read, verify, Planned, Generate(path, []), Write(path));
    }

    /// <summary>Read: the document is read. Verify: its workflow runs, with no gaps, and passes against a twin.</summary>
    private static async Task<(string, string, string, string, string, string, string)> Arazzo(string version)
    {
        var path = Path.Combine(Versions, $"arazzo-{version}.yaml");
        var spec = Path.Combine(Versions, "openapi-3.1.yaml");
        if (!Probe(() => SpecJson.Arazzo(File.ReadAllText(path), path), out var arazzo, out var read))
        {
            return ("Arazzo", version, read, "", NotApplicable, NotApplicable, Write(path));
        }

        var json = SpecJson.Normalize(File.ReadAllText(spec), spec);
        var suite = RegressionSuite.FromJson(json, arazzo!);
        if (suite.Gaps.Count > 0)
        {
            return ("Arazzo", version, read, $"✗ {suite.Gaps[0]}", NotApplicable, NotApplicable, Write(path));
        }

        using var client = new HttpClient(new SpecTwin(json)) { BaseAddress = new Uri("http://twin/") };
        var report = await suite.RunAsync(client, new RegressionOptions { Include = _ => false, MaxRetryWait = TimeSpan.Zero });
        var verify = report.Workflows.Count > 0 && report.Failures.Count == 0 ? $"✓ {report.Workflows.Count} workflow" : $"✗ {report.Failures.Count} failures";
        return ("Arazzo", version, read, verify, NotApplicable, NotApplicable, Write(path));
    }

    /// <summary>Read: the overlay applies. Verify: what it adds reaches the suite. Generate: the overlaid spec compiles.</summary>
    private static (string, string, string, string, string, string, string) Overlay(string version)
    {
        var path = Path.Combine(Versions, $"overlay-{version}.yaml");
        var spec = Path.Combine(Versions, "openapi-3.1.yaml");
        if (!Probe(() => SpecJson.Normalize(File.ReadAllText(spec), spec, out _, [(File.ReadAllText(path), path)]), out var json, out var read))
        {
            return ("Overlay", version, read, "", NotApplicable, "", Write(path));
        }

        var verify = RegressionSuite.FromJson(json!).Spec.Operation("createPet").Json["x-matchmark-ensures"] is not null ? "✓" : "✗ not applied";
        return ("Overlay", version, read, verify, NotApplicable, Generate(spec, [path]), Write(path));
    }

    /// <summary>Write: <c>matchmark upgrade</c> writes it at the latest version of its format, with nothing lost.</summary>
    private static string Write(string path)
    {
        try
        {
            var result = SpecUpgrade.Upgrade(File.ReadAllText(path), path);
            return result.Losses.Count == 0 ? $"✓ to {result.To}" : $"✗ {result.Losses.Count} lost";
        }
        catch (InvalidOperationException)
        {
            return "✗ not upgraded";
        }
    }

    private static string Generate(string spec, string[] overlays)
    {
        try
        {
            var (_, diagnostics) = GeneratedServer.Build("Shelter", spec, overlays, string.Empty, withRules: false);
            var skipped = diagnostics.Count(d => d.Id is "MM001" or "MM002" or "MM003");
            return skipped == 0 ? "✓" : $"✗ {skipped} skipped";
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return "✗ doesn't compile";
        }
    }

    private static bool Probe<T>(Func<T> read, out T? value, out string cell)
    {
        try
        {
            value = read();
            cell = "✓";
            return true;
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or SharpYaml.YamlException or System.Text.Json.JsonException)
        {
            value = default;
            cell = "✗ not read";
            return false;
        }
    }

    private static string Render(List<(string Format, string Version, string Read, string Verify, string Host, string Generate, string Write)> rows)
    {
        var page = new StringBuilder();
        page.Append("""
            # Support matrix

            Generated by `SupportMatrixTests` from the documents in [samples/versions](../../samples/versions). Don't edit it by
            hand: when a result changes, the test writes this page and fails until it's committed.

            | Column | What passing means |
            |:--|:--|
            | Read | The document is read, forward-ported where it's an older version. |
            | Verify | OpenAPI: a regression suite is generated. AsyncAPI: a consumer suite is generated. Arazzo: its workflow runs with no gaps and passes against a twin. Overlay: what it adds reaches the suite. |
            | Host | The suite passes against a twin of the API (OpenAPI). Event channels per session are planned. |
            | Generate | The generated code compiles with nothing skipped (`MM001` to `MM003`). |
            | Write | `matchmark upgrade` writes it at the latest version of its format, with nothing lost. |

            `✓` passes, `✗` fails with the reason, `planned` isn't built yet, `n/a` doesn't apply to the format.

            | Format | Version | Read | Verify | Host | Generate | Write |
            |:--|:--|:--|:--|:--|:--|:--|

            """.ReplaceLineEndings("\n"));
        foreach (var (format, version, read, verify, host, generate, write) in rows)
        {
            page.Append(Row(format, version, read, verify, host, generate, write));
        }

        return page.ToString();
    }

    private static string Row(params string[] cells) => "| " + string.Join(" | ", cells.Select(c => c.Length == 0 ? "–" : c)) + " |\n";

    private static string ThisDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
