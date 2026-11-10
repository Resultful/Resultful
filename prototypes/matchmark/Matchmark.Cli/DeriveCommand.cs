using Matchmark.Generator;
using Matchmark.Testing;

namespace Matchmark.Cli;

/// <summary><c>derive</c>: step 1 of the flip, a starting spec from the document ASP.NET Core serves (docs/reference/flip.md).</summary>
public static class DeriveCommand
{
    public const string Usage = """
        usage: matchmark derive <url or file> [--output <file>]
               matchmark derive --har <recording.har> [--title <title>] [--output <file>]

          Turns the document an ASP.NET Core application serves (Microsoft.AspNetCore.OpenApi, /openapi/v1.json) into a
          starting spec: every operation gets an operationId, and what to review before the flip is listed, with what
          matchmark audit finds. The document records accidents as well as decisions: fix them in the spec, then verify
          the application against it with matchmark regress.
          --har     instead, infer the spec from recorded traffic (an HTTP Archive, as browsers and proxies export it), for
                    an application that isn't ASP.NET Core: paths templated where a segment is an id, schemas from every
                    body seen, unions where a field's values always bring their own fields, and each status seen
          --output  the file to write (.json for JSON, anything else YAML); without it, YAML goes to standard output
        """;

    public static async Task<int> RunAsync(string[] args, TextWriter output, CancellationToken cancellationToken = default)
    {
        if (args.Length < 1 || args[0].StartsWith("--", StringComparison.Ordinal))
        {
            await output.WriteLineAsync(Usage);
            return 2;
        }

        System.Text.Json.Nodes.JsonObject spec;
        IReadOnlyList<string> review;
        if (Option(args, "--har") is { } har)
        {
            try
            {
                (spec, review) = TrafficDerive.Derive(TrafficDerive.Har(await File.ReadAllTextAsync(har, cancellationToken)), Option(args, "--title") ?? Path.GetFileNameWithoutExtension(har));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException)
            {
                await output.WriteLineAsync($"error: {e.Message}");
                return 2;
            }

            return await WriteAsync(args, output, spec, review, cancellationToken);
        }

        string document;
        try
        {
            if (Uri.TryCreate(args[0], UriKind.Absolute, out var url) && url.Scheme is "http" or "https")
            {
                using var client = new HttpClient();
                document = await client.GetStringAsync(url, cancellationToken);
            }
            else
            {
                document = await File.ReadAllTextAsync(args[0], cancellationToken);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or HttpRequestException)
        {
            await output.WriteLineAsync($"error: {e.Message}");
            return 2;
        }

        try
        {
            (spec, review) = SpecDerive.Derive(SpecJson.Normalize(document, args[0].EndsWith(".json", StringComparison.OrdinalIgnoreCase) || !File.Exists(args[0]) ? "openapi.json" : args[0]));
        }
        catch (Exception e) when (e is InvalidOperationException or System.Text.Json.JsonException or SharpYaml.YamlException)
        {
            await output.WriteLineAsync($"error: {e.Message}");
            return 2;
        }

        return await WriteAsync(args, output, spec, review, cancellationToken);
    }

    private static async Task<int> WriteAsync(string[] args, TextWriter output, System.Text.Json.Nodes.JsonObject spec, IReadOnlyList<string> review, CancellationToken cancellationToken)
    {
        var audit = Audit.Run(spec.ToJsonString()).GroupBy(f => f.Rule.Id).Select(g => $"audit {g.Key}: {g.Count()} (matchmark audit lists them)");
        var lines = review.Concat(audit).ToList();
        if (Option(args, "--output") is { } file)
        {
            await File.WriteAllTextAsync(file, file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? spec.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true })
                : SpecUpgrade.Yaml(spec).TrimStart('\n'), cancellationToken);
            foreach (var line in lines)
            {
                await output.WriteLineAsync(line);
            }

            await output.WriteLineAsync($"Wrote {file}: review the {review.Count} points above before the flip.");
        }
        else
        {
            foreach (var line in lines)
            {
                await output.WriteLineAsync($"# {line}");
            }

            await output.WriteLineAsync(SpecUpgrade.Yaml(spec).TrimStart('\n'));
        }

        return 0;
    }

    private static string? Option(string[] args, string name)
        => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
