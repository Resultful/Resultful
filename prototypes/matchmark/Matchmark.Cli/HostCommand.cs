using Matchmark.Conformance;
using Matchmark.Generator;
using Matchmark.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Matchmark.Cli;

/// <summary><c>host</c>: the session host, a copy of the API per test session (docs/reference/sessions.md).</summary>
public static class HostCommand
{
    public const string Usage = """
        usage: matchmark host <spec> [--arazzo <file>]... [--overlay <file>]... [--provider <file>] [--urls <url>] [--service <url>]

          Serves a copy of the API per test session, from the spec alone. A consumer starts a session with
          PUT /__fake/sessions/{id} and calls it at {id}.localhost (or with the header X-Matchmark-Session: {id}).
          Each copy is a twin of the API, seeded per session, with the session's token in the spec's security scheme.
          --arazzo    pathways: Arazzo workflows a session can require its consumer to cover (repeat for more)
          --overlay   an OpenAPI Overlay applied to the spec first (repeat for more)
          --provider  the provider's results for the pathways, from matchmark regress --save, for the report
          --urls      where to listen (default http://localhost:8080)
          --service   the real service, in another process, for sessions started with "copy": "service": requests are
                      forwarded to it, and it starts and ends each session's data at PUT and DELETE /__matchmark/sessions/{id}
        """;

    public static async Task<int> RunAsync(string[] args, TextWriter output, CancellationToken cancellationToken = default)
    {
        if (args.Length < 1 || args[0].StartsWith("--", StringComparison.Ordinal))
        {
            await output.WriteLineAsync(Usage);
            return 2;
        }

        SessionHost host;
        try
        {
            var overlays = new List<(string, string)>();
            var arazzo = new List<string>();
            for (var i = 1; i < args.Length - 1; i++)
            {
                if (args[i] == "--overlay")
                {
                    overlays.Add((await File.ReadAllTextAsync(args[i + 1], cancellationToken), args[i + 1]));
                }
                else if (args[i] == "--arazzo")
                {
                    arazzo.Add(SpecJson.Arazzo(await File.ReadAllTextAsync(args[i + 1], cancellationToken), args[i + 1]));
                }
            }

            var service = Option(args, "--service") is { } url ? ServiceCopy.Http(new Uri(url.EndsWith('/') ? url : url + "/")) : null;
            host = new SessionHost(SpecJson.Normalize(await File.ReadAllTextAsync(args[0], cancellationToken), args[0], out _, overlays), arazzo) { Service = service };
            if (Option(args, "--provider") is { } provider)
            {
                host.Provider = ProviderResults.FromJson(await File.ReadAllTextAsync(provider, cancellationToken));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or UriFormatException or SharpYaml.YamlException or System.Text.Json.JsonException)
        {
            await output.WriteLineAsync($"error: {e.Message}");
            return 2;
        }

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls(Option(args, "--urls") ?? "http://localhost:8080");
        var app = builder.Build();
        app.UseMatchmarkSessions(host);
        await app.StartAsync(cancellationToken);
        await output.WriteLineAsync($"Sessions of {args[0]} at {string.Join(", ", app.Urls)}: PUT /__fake/sessions/{{id}} to start one. Pathways: {(host.PathwayIds.Count == 0 ? "none" : string.Join(", ", host.PathwayIds))}. Ctrl+C stops it.");
        await Microsoft.Extensions.Hosting.HostingAbstractionsHostExtensions.WaitForShutdownAsync(app, cancellationToken);
        return 0;
    }

    private static string? Option(string[] args, string name)
        => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
