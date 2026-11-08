using System.Globalization;
using Matchmark.Conformance;
using Matchmark.Generator;
using Matchmark.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Matchmark.Cli;

/// <summary><c>twin</c> serves a stateful fake of a spec; <c>fidelity</c> measures a fake against the real API.</summary>
public static class TwinCommands
{
    public const string TwinUsage = """
        usage: matchmark twin <spec> [--overlay <file>]... [--urls <url>]

          Serves a twin of the API: a stateful fake from the spec alone. /pets and /pets/{petId} are one collection, so
          what is created can be read, listed, changed and deleted; requests are checked as the spec says. For a frontend
          to work against before the API exists. Default URL: http://localhost:5080.
        """;

    public const string FidelityUsage = """
        usage: matchmark fidelity <spec> <real url> <fake url> [--overlay <file>]... [--floor <percent>]

          Runs the suite generated from the spec against the real API and against a fake (a twin, say), both from the
          same starting state, and reports the share of steps that answer the same, per operation. Exits 1 below --floor.
        """;

    public static async Task<int> TwinAsync(string[] args, TextWriter output, CancellationToken cancellationToken = default)
    {
        if (args.Length < 1)
        {
            await output.WriteLineAsync(TwinUsage);
            return 2;
        }

        var spec = await Spec(args, cancellationToken);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls(Option(args, "--urls") ?? "http://localhost:5080");
        var app = builder.Build();
        app.UseMatchmarkTwin(spec);
        await app.StartAsync(cancellationToken);
        await output.WriteLineAsync($"A twin of {args[0]} is listening on {string.Join(", ", app.Urls)}. Ctrl+C stops it.");
        await Microsoft.Extensions.Hosting.HostingAbstractionsHostExtensions.WaitForShutdownAsync(app, cancellationToken);
        return 0;
    }

    public static async Task<int> FidelityAsync(string[] args, TextWriter output, CancellationToken cancellationToken = default)
    {
        if (args.Length < 3 || !Uri.TryCreate(Slash(args[1]), UriKind.Absolute, out var real) || !Uri.TryCreate(Slash(args[2]), UriKind.Absolute, out var fake))
        {
            await output.WriteLineAsync(FidelityUsage);
            return 2;
        }

        var suite = RegressionSuite.FromJson(await Spec(args, cancellationToken));
        using var realClient = new HttpClient { BaseAddress = real };
        using var fakeClient = new HttpClient { BaseAddress = fake };
        FidelityReport fidelity;
        try
        {
            var expected = await suite.RunAsync(realClient, cancellationToken: cancellationToken);
            fidelity = (await suite.RunAsync(fakeClient, cancellationToken: cancellationToken)).Fidelity(expected);
        }
        catch (HttpRequestException e)
        {
            await output.WriteLineAsync($"error: an API can't be reached: {e.Message}");
            return 2;
        }

        await output.WriteLineAsync(fidelity.ToString());
        var floor = Option(args, "--floor") is { } text && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent) ? percent / 100 : 0;
        return fidelity.Score < floor ? 1 : 0;
    }

    private static async Task<string> Spec(string[] args, CancellationToken cancellationToken)
    {
        var overlays = new List<(string, string)>();
        for (var i = 1; i < args.Length - 1; i++)
        {
            if (args[i] == "--overlay")
            {
                overlays.Add((await File.ReadAllTextAsync(args[i + 1], cancellationToken), args[i + 1]));
            }
        }

        return SpecJson.Normalize(await File.ReadAllTextAsync(args[0], cancellationToken), args[0], out _, overlays);
    }

    private static string? Option(string[] args, string name)
        => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;

    private static string Slash(string url) => url.EndsWith('/') ? url : url + "/";
}
