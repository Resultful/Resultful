using Matchmark.Generator;

namespace Matchmark.Cli;

/// <summary><c>upgrade</c>: a spec forward-ported to the latest version of its format, with what changed and what was lost.</summary>
public static class UpgradeCommand
{
    public const string Usage = """
        usage: matchmark upgrade <spec> [--output <file>] [--to <version>]

          Writes the spec forward-ported to the latest version of its format: OpenAPI 3.2, AsyncAPI 3.1, Arazzo 1.1 or
          Overlay 1.2. --to writes OpenAPI 3.1 instead, for ASP.NET Core 10. Lists every change, and anything the target
          version has no place for; exits 1 when something was lost.
          --output  the file to write (.json for JSON, anything else YAML); without it, YAML goes to standard output
                    with the changes as comments at the top
          --to      the version to write: 3.2 or 3.1 for OpenAPI; the latest only for the other formats
        """;

    public static async Task<int> RunAsync(string[] args, TextWriter output, CancellationToken cancellationToken = default)
    {
        if (args.Length < 1 || args[0].StartsWith("--", StringComparison.Ordinal))
        {
            await output.WriteLineAsync(Usage);
            return 2;
        }

        SpecUpgrade.Result result;
        try
        {
            result = SpecUpgrade.Upgrade(await File.ReadAllTextAsync(args[0], cancellationToken), args[0], Option(args, "--to"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or SharpYaml.YamlException or System.Text.Json.JsonException)
        {
            await output.WriteLineAsync($"error: {e.Message}");
            return 2;
        }

        var report = new List<string> { $"{result.Format} {result.From} to {result.To}" };
        report.AddRange(result.Changes.Select(c => $"changed: {c}"));
        report.AddRange(result.Losses.Select(l => $"lost: {l}"));
        if (Option(args, "--output") is { } file)
        {
            var text = file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? result.Document.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true })
                : SpecUpgrade.Yaml(result.Document).TrimStart('\n');
            await File.WriteAllTextAsync(file, text, cancellationToken);
            foreach (var line in report)
            {
                await output.WriteLineAsync(line);
            }

            await output.WriteLineAsync($"Wrote {file}.");
        }
        else
        {
            foreach (var line in report)
            {
                await output.WriteLineAsync($"# {line}");
            }

            await output.WriteAsync(SpecUpgrade.Yaml(result.Document).TrimStart('\n'));
        }

        return result.Losses.Count > 0 ? 1 : 0;
    }

    private static string? Option(string[] args, string name)
        => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
