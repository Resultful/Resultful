using Matchmark.Generator;
using Matchmark.Testing;

namespace Matchmark.Cli;

/// <summary>
/// <c>regress &lt;spec&gt; &lt;base url&gt;</c>: generates the regression suite from a spec and runs it against any HTTP API,
/// whatever it is written in. Prints the report; the exit code is 0 when nothing failed.
/// </summary>
public static partial class Regress
{
    public const string Usage = """
        usage: matchmark regress <spec> <base url> [--arazzo <file>]... [--overlay <file>]... [--no-workflows] [--webhooks] [--random <n>] [--seed <n>] [--save <file>]

          Generates a regression suite from the spec (examples, valid requests, boundary values, one invalid request per
          constraint, and workflows from its links) and runs it against the API at <base url>, checking every response.
          --arazzo        an Arazzo document whose workflows run too (repeat for more)
          --overlay       an OpenAPI Overlay applied to the spec first (repeat for more, applied in order)
          --no-workflows  don't follow links or run workflows (for a target that keeps no state, such as a fake)
          --webhooks      <base url> is a receiver of the spec's webhooks (each at /{name}): test it instead of the API
          --random        also send n random valid requests per operation, shrinking each failure (--seed to repeat a run)
          --save          write each workflow's result, as the provider's half of hosted sessions' reports (matchmark host)

          An AsyncAPI 3 document in place of the spec tests a consumer of its messages: <base url> is a bridge that hands
          each POST /{operationId} to the consumer as a message (the body its payload, the headers its headers) and
          answers 2xx when the consumer takes it, 400 when it refuses it.
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

            var text = await File.ReadAllTextAsync(args[0], cancellationToken);
            if (AsyncApi().IsMatch(text))
            {
                problems = [];
                suite = AsyncApiDocument.Parse(SpecJson.AsyncApi(text, args[0])).ConsumerSuite()
                    ?? throw new InvalidOperationException($"{args[0]} receives no messages, so there's no consumer to test.");
            }
            else if (SpecJson.Normalize(text, args[0], out problems, overlays) is var json && args.Contains("--webhooks"))
            {
                suite = RegressionSuite.ForWebhooks(json) ?? throw new InvalidOperationException($"{args[0]} declares no webhooks.");
            }
            else
            {
                suite = RegressionSuite.FromJson(json, [.. arazzo]);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or SharpYaml.YamlException or System.Text.Json.JsonException)
        {
            await output.WriteLineAsync($"error: {e.Message}");
            return 2;
        }

        foreach (var problem in problems)
        {
            await output.WriteLineAsync($"warning: the reader objected, and that part may be missing: {problem}");
        }

        using var client = new HttpClient { BaseAddress = baseAddress };
        await output.WriteLineAsync($"{suite} against {baseAddress}");
        foreach (var gap in suite.Gaps)
        {
            await output.WriteLineAsync($"gap: {gap}");
        }

        static int Number(string[] args, string name, int fallback)
            => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : fallback;

        var options = new RegressionOptions { Workflows = !args.Contains("--no-workflows"), Random = Number(args, "--random", 0), Seed = Number(args, "--seed", 1) };
        RegressionReport report;
        try
        {
            report = await suite.RunAsync(client, options, cancellationToken);
        }
        catch (HttpRequestException e)
        {
            await output.WriteLineAsync($"error: {baseAddress} can't be reached: {e.Message}");
            return 2;
        }

        await output.WriteLineAsync(report.ToString());
        if (Array.IndexOf(args, "--save") is var save and >= 0 && save + 1 < args.Length)
        {
            // The provider's results for the workflows run, for the provider half of hosted sessions' reports.
            await File.WriteAllTextAsync(args[save + 1], ProviderResults.Of(report).ToJson(), cancellationToken);
        }

        return report.Passed ? 0 : 1;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^\s*[""']?asyncapi[""']?\s*:", System.Text.RegularExpressions.RegexOptions.Multiline)]
    private static partial System.Text.RegularExpressions.Regex AsyncApi();
}
