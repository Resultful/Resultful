using System.Runtime.CompilerServices;
using AwesomeAssertions;
using NUnit.Framework;

namespace Matchmark.Testing.Tests;

/// <summary>
/// The standard readers (Q30, Q31) measured against the corpus, so Matchmark's own readers are retired on evidence
/// (Q32): every AsyncAPI and Arazzo document Matchmark reads is read by the library too, and what the library refuses
/// is pinned. A pin that stops holding fails here, so a library that gains Arazzo 1.1, or loses something, is noticed.
/// </summary>
[TestFixture]
public sealed class ReaderLibraryTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(ThisDirectory(), ".."));

    /// <summary>Every AsyncAPI document in the corpus, by its path from the prototype's root.</summary>
    public static IEnumerable<string> AsyncApiDocuments()
        => ["samples/versions/asyncapi-2.6.yaml", "samples/versions/asyncapi-3.0.yaml", "samples/versions/asyncapi-3.1.yaml", "samples/notifications/asyncapi.yaml"];

    /// <summary>Every Arazzo document in the corpus, with what BinkyLabs makes of it today.</summary>
    public static IEnumerable<TestCaseData> ArazzoDocuments()
    {
        // Arazzo 1.1 (message steps, Q31's fork) isn't supported by preview.4.
        const string Unsupported = "unsupported";
        // Redocly's museum workflows refer to their sources loosely: operationPath as $sourceDescriptions.museum-api#/paths/...
        // rather than {$sourceDescriptions.museum-api.url}#/paths/..., and an external workflowId without the form the
        // specification gives. BinkyLabs objects to each; Matchmark accepts them.
        const string Objects = "objects";
        return new (string Path, string Expected)[]
        {
            ("samples/versions/arazzo-1.0.yaml", "reads"),
            ("samples/versions/arazzo-1.1.yaml", Unsupported),
            ("samples/orders/orders.arazzo.yaml", "reads"),
            ("samples/museum/arazzo/museum-tickets.arazzo.yaml", "reads"),
            ("samples/museum/arazzo/museum-api.arazzo.yaml", Objects),
            ("samples/notifications/notifications.arazzo.yaml", Unsupported),
            ("samples/sessions/shelter.arazzo.yaml", Unsupported),
        }.Select(d => new TestCaseData(d.Path, d.Expected).SetName($"Arazzo {d.Path} {d.Expected}"));
    }

    [Test]
    public void The_pins_cover_every_document_in_the_corpus()
    {
        var arazzo = Directory.GetFiles(Path.Combine(Root, "samples"), "*.arazzo.yaml", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(Root, "samples", "versions"), "arazzo-*.yaml"))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}examples{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(Root, f).Replace('\\', '/')).Order(StringComparer.Ordinal).ToList();
        var asyncApi = Directory.GetFiles(Path.Combine(Root, "samples"), "asyncapi*.yaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}examples{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(Root, f).Replace('\\', '/')).Order(StringComparer.Ordinal).ToList();

        arazzo.Should().HaveCountGreaterThan(5).And.BeEquivalentTo(ArazzoDocuments().Select(d => (string)d.Arguments[0]!));
        asyncApi.Should().HaveCountGreaterThan(3).And.BeEquivalentTo(AsyncApiDocuments());
    }

    [TestCaseSource(nameof(AsyncApiDocuments))]
    public async Task The_AsyncAPI_SDK_reads_every_AsyncAPI_document(string path)
    {
        var reader = new Neuroglia.AsyncApi.IO.AsyncApiDocumentReader(Neuroglia.Serialization.Json.JsonSerializer.Default, Neuroglia.Serialization.Yaml.YamlSerializer.Default);
        await using var stream = File.OpenRead(Path.Combine(Root, path));
        var document = await reader.ReadAsync(stream);

        document.Should().NotBeNull();
        var version = (string?)document!.GetType().GetProperty("AsyncApi")?.GetValue(document);
        var expected = Path.GetFileNameWithoutExtension(path).Replace("asyncapi-", string.Empty, StringComparison.Ordinal);
        version.Should().StartWith(expected == "asyncapi" ? "3.0" : expected, "the SDK read the document's own version");
    }

    [TestCaseSource(nameof(ArazzoDocuments))]
    public async Task BinkyLabs_reads_the_Arazzo_documents_it_supports(string path, string expected)
    {
        var result = await BinkyLabs.OpenApi.Arazzo.ArazzoDocument.LoadFromUrlAsync(Path.Combine(Root, path), new BinkyLabs.OpenApi.Arazzo.ArazzoReaderSettings());
        var errors = result.Diagnostic?.Errors.Select(e => e.Message).ToList() ?? [];
        var actual = result.Document is null && errors.Any(e => e.Contains("version '1.1.0' is not supported", StringComparison.Ordinal)) ? "unsupported"
            : result.Document is not null && errors.Count == 0 ? "reads"
            : result.Document is not null && errors.All(e => e.Contains("$sourceDescriptions", StringComparison.Ordinal)) ? "objects"
            : $"other: {string.Join(" | ", errors)}";

        actual.Should().Be(expected, "a changed pin means the library changed: retire Matchmark's reader if it now reads everything (Q32)");
    }

    private static string ThisDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
