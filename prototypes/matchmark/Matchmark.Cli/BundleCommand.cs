using System.Text.Json.Nodes;
using Matchmark.Generator;

namespace Matchmark.Cli;

/// <summary>
/// <c>bundle</c>: one spec from a spec split across files (question 2, option b). Every <c>$ref</c> to another file is
/// resolved, relative to the file that makes it, and its target hoisted into the components section its place calls
/// for (a schema, parameter or response), named after its pointer or its file, so the generator, which reads one file,
/// can read the result. References inside a referenced file are resolved the same way; a cycle ends at the hoisted name.
/// </summary>
public static class BundleCommand
{
    public const string Usage = """
        usage: matchmark bundle <spec> [--output <file>]

          Writes a spec split across files (a $ref to ../common/Error.yaml, or parameters.yaml#/tagsParam) as one file: each
          referenced schema, parameter or response becomes a component named after it, so the generator can read it.
          --output  the file to write (.json for JSON, anything else YAML); without it, YAML goes to standard output
        """;

    public static async Task<int> RunAsync(string[] args, TextWriter output, CancellationToken cancellationToken = default)
    {
        if (args.Length < 1 || args[0].StartsWith("--", StringComparison.Ordinal))
        {
            await output.WriteLineAsync(Usage);
            return 2;
        }

        JsonObject bundled;
        try
        {
            bundled = Bundle(args[0]);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or SharpYaml.YamlException or System.Text.Json.JsonException)
        {
            await output.WriteLineAsync($"error: {e.Message}");
            return 2;
        }

        var file = Array.IndexOf(args, "--output") is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        var text = file?.EndsWith(".json", StringComparison.OrdinalIgnoreCase) == true
            ? bundled.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true })
            : SpecUpgrade.Yaml(bundled).TrimStart('\n');
        if (file is null)
        {
            await output.WriteLineAsync(text);
        }
        else
        {
            await File.WriteAllTextAsync(file, text, cancellationToken);
            await output.WriteLineAsync($"Wrote {file}.");
        }

        return 0;
    }

    /// <summary>The spec at <paramref name="path"/>, with every reference to another file brought into it.</summary>
    public static JsonObject Bundle(string path)
        => SpecBundle.Bundle(Path.GetFullPath(path), file => File.Exists(file) ? File.ReadAllText(file) : null);
}
