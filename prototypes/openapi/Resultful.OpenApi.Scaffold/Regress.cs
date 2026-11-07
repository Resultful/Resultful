using Resultful.OpenApi.Generator;
using Resultful.OpenApi.Testing;

namespace Resultful.OpenApi.Scaffold;

/// <summary>
/// <c>regress &lt;spec&gt; &lt;base url&gt;</c>: generates the regression suite from a spec and runs it against any HTTP API,
/// whatever it is written in. Prints the report; the exit code is 0 when nothing failed.
/// </summary>
public static class Regress
{
    public const string Usage = """
        usage: scaffold regress <spec> <base url> [--no-workflows]

          Generates a regression suite from the spec (examples, valid requests, boundary values, one invalid request per
          constraint, and workflows from its links) and runs it against the API at <base url>, checking every response.
          --no-workflows  don't follow links (for a target that keeps no state, such as a fake)
        """;

    public static async Task<int> RunAsync(string[] args, TextWriter output, CancellationToken cancellationToken = default)
    {
        if (args.Length < 2 || !Uri.TryCreate(args[1].EndsWith('/') ? args[1] : args[1] + "/", UriKind.Absolute, out var baseAddress))
        {
            await output.WriteLineAsync(Usage);
            return 2;
        }

        var suite = RegressionSuite.FromJson(SpecJson.Normalize(await File.ReadAllTextAsync(args[0], cancellationToken), args[0]));
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
