using Matchmark.Generator;
using Matchmark.Testing;

namespace Matchmark.Cli;

/// <summary>
/// <c>regress &lt;spec&gt; &lt;base url&gt;</c>: generates the regression suite from a spec and runs it against any HTTP API,
/// whatever it is written in. Prints the report; the exit code is 0 when nothing failed.
/// </summary>
public static class Regress
{
    public const string Usage = """
        usage: matchmark regress <spec> <base url> [--arazzo <file>]... [--overlay <file>]... [--no-workflows]

          Generates a regression suite from the spec (examples, valid requests, boundary values, one invalid request per
          constraint, and workflows from its links) and runs it against the API at <base url>, checking every response.
          --arazzo        an Arazzo document whose workflows run too (repeat for more)
          --overlay       an OpenAPI Overlay applied to the spec first (repeat for more, applied in order)
          --no-workflows  don't follow links or run workflows (for a target that keeps no state, such as a fake)
        """;

    public static async Task<int> RunAsync(string[] args, TextWriter output, CancellationToken cancellationToken = default)
    {
        if (args.Length < 2 || !Uri.TryCreate(args[1].EndsWith('/') ? args[1] : args[1] + "/", UriKind.Absolute, out var baseAddress))
        {
            await output.WriteLineAsync(Usage);
            return 2;
        }

        RegressionSuite suite;
        IReadOnlyList<string> problems;
        try
        {
            var arazzo = new List<string>();
            var overlays = new List<(string, string)>();
            for (var i = 2; i < args.Length - 1; i++)
            {
                if (args[i] == "--arazzo")
                {
                    arazzo.Add(SpecJson.Arazzo(await File.ReadAllTextAsync(args[i + 1], cancellationToken), args[i + 1]));
                }
                else if (args[i] == "--overlay")
                {
                    overlays.Add((await File.ReadAllTextAsync(args[i + 1], cancellationToken), args[i + 1]));
                }
            }

            suite = RegressionSuite.FromJson(SpecJson.Normalize(await File.ReadAllTextAsync(args[0], cancellationToken), args[0], out problems, overlays), [.. arazzo]);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or SharpYaml.YamlException or System.Text.Json.JsonException)
        {
            await output.WriteLineAsync($"error: {e.Message}");
            return 2;
        }

        foreach (var problem in problems)
        {
            await output.WriteLineAsync($"warning: the reader objected, and that part may be missing: {problem}");
        }

        using var client = new HttpClient { BaseAddress = baseAddress };
        var report = await suite.RunAsync(client, new RegressionOptions { Workflows = !args.Contains("--no-workflows") }, cancellationToken);

        await output.WriteLineAsync($"{suite} against {baseAddress}");
        foreach (var gap in suite.Gaps)
        {
            await output.WriteLineAsync($"gap: {gap}");
        }

        await output.WriteLineAsync(report.ToString());
        return report.Passed ? 0 : 1;
    }
}
