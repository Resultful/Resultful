using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Matchmark.Specs.Tests;

/// <summary>Where the vendored specifications are, and the documents in them.</summary>
internal static partial class Specs
{
    public static readonly string Root = Path.GetFullPath(Path.Combine(Here(), "..", "specs"));

    public static string PathOf(params string[] parts) => Path.Combine([Root, .. parts]);

    /// <summary>Every file under a vendored folder with one of the extensions, relative to specs/, in a stable order.</summary>
    public static IEnumerable<string> Files(string folder, params string[] extensions)
        => Directory.GetFiles(PathOf(folder), "*", SearchOption.AllDirectories)
            .Where(f => extensions.Any(e => f.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
            .Select(f => Path.GetRelativePath(Root, f).Replace('\\', '/'))
            .Order(StringComparer.Ordinal);

    [GeneratedRegex(@"```(?:yaml|yml|json)\n(?<body>.*?)\n```", RegexOptions.Singleline)]
    private static partial Regex CodeBlock();

    /// <summary>The fenced YAML and JSON examples in a specification's text, in order.</summary>
    public static IEnumerable<string> Examples(string text) => CodeBlock().Matches(text.Replace("\r\n", "\n")).Select(m => m.Groups["body"].Value);

    public static JsonNode Load(string relative) => Documents.Load(File.ReadAllText(PathOf(relative)), relative);

    private static string Here([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
