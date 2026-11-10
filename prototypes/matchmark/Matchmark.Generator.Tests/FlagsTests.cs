using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Matchmark.Testing;
using Microsoft.AspNetCore.Builder;
using NUnit.Framework;

namespace Matchmark.Generator.Tests;

/// <summary>
/// Concurrency and partial updates, with the Flags sample: response headers set by the handler (an ETag on every read),
/// If-Match on every change, JSON Merge Patch, a version only the server sets and that only goes up. The generated
/// server held to its suite and overlay; the client round trip; and the twin measured against the generated server.
/// </summary>
[TestFixture]
public sealed class FlagsTests
{
    private static readonly string Samples = Path.GetFullPath(Path.Combine(ThisDirectory(), "..", "samples", "flags"));

    private const string Source = """
        using System;
        using System.Collections.Concurrent;
        using System.Collections.Generic;
        using System.Linq;
        using System.Net.Http;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Hosting;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Mvc;
        using Microsoft.Extensions.DependencyInjection;
        using Resultful;

        namespace Flags.Api;

        public sealed class Handlers : IFlagsHandlers
        {
            private readonly ConcurrentDictionary<string, Flag> _flags = new();

            private static string ETag(Flag flag) => $"\"v{flag.Version}\"";

            public Task<IReadOnlyList<Flag>> ListFlags(CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<Flag>>(_flags.Values.OrderBy(f => f.Key).ToList());

            public Task<Result<PutFlagSuccess, PutFlagError>> PutFlag(string key, NewFlag body, CancellationToken cancellationToken)
            {
                var existed = _flags.TryGetValue(key, out var before);
                var flag = new Flag { Key = key, Enabled = body.Enabled, Rollout = body.Rollout ?? (body.Enabled ? 100 : 0), Description = body.Description, Version = (before?.Version ?? 0) + 1 };

                // PUT is idempotent (RFC 9110): the same flag again changes nothing, its version included.
                if (before is not null && flag with { Version = before.Version } == before)
                {
                    flag = before;
                }

                _flags[key] = flag;
                FlagsHeaders.SetETag(ETag(flag));
                return Task.FromResult(Result.Ok<PutFlagSuccess, PutFlagError>(existed ? PutFlagSuccess.Ok(flag) : PutFlagSuccess.Created(flag)));
            }

            public Task<Result<Flag, GetFlagError>> GetFlag(string key, CancellationToken cancellationToken)
            {
                if (!_flags.TryGetValue(key, out var flag))
                {
                    return Task.FromResult(Result.Fail<Flag, GetFlagError>(GetFlagError.NotFound(new ProblemDetails { Title = "No such flag" })));
                }

                FlagsHeaders.SetETag(ETag(flag));
                return Task.FromResult(Result.Ok<Flag, GetFlagError>(flag));
            }

            public Task<Result<Flag, PatchFlagError>> PatchFlag(string key, string ifMatch, FlagPatch body, CancellationToken cancellationToken)
            {
                if (!_flags.TryGetValue(key, out var flag))
                {
                    return Task.FromResult(Result.Fail<Flag, PatchFlagError>(PatchFlagError.NotFound(new ProblemDetails { Title = "No such flag" })));
                }

                if (ifMatch != ETag(flag))
                {
                    return Task.FromResult(Result.Fail<Flag, PatchFlagError>(PatchFlagError.PreconditionFailed(new ProblemDetails { Title = "The flag changed since that ETag" })));
                }

                var patched = flag with { Enabled = body.Enabled ?? flag.Enabled, Rollout = body.Rollout ?? flag.Rollout, Description = body.Description ?? flag.Description, Version = flag.Version + 1 };
                if (patched.Enabled && patched.Rollout == 0)
                {
                    var problem = new HttpValidationProblemDetails(new Dictionary<string, string[]> { ["rollout"] = ["An enabled flag is rolled out to someone."] }) { Status = 400 };
                    return Task.FromResult(Result.Fail<Flag, PatchFlagError>(PatchFlagError.BadRequest(problem)));
                }

                _flags[key] = patched;
                FlagsHeaders.SetETag(ETag(patched));
                return Task.FromResult(Result.Ok<Flag, PatchFlagError>(patched));
            }

            public Task<VoidResult<DeleteFlagError>> DeleteFlag(string key, CancellationToken cancellationToken)
                => Task.FromResult(_flags.TryRemove(key, out _)
                    ? Result.Ok<DeleteFlagError>()
                    : Result.Fail<DeleteFlagError>(DeleteFlagError.NotFound(new ProblemDetails { Title = "No such flag" })));
        }

        public static class Server
        {
            public static async Task<WebApplication> StartAsync()
            {
                var builder = WebApplication.CreateBuilder();
                builder.WebHost.UseUrls("http://127.0.0.1:0");
                builder.Services.AddFlags();
                builder.Services.AddProblemDetails();
                builder.Services.AddSingleton<IFlagsHandlers, Handlers>();
                var app = builder.Build();
                app.MapFlags();
                await app.StartAsync();
                return app;
            }

            /// <summary>Put, then change with the ETag as If-Match, then change again with the same, now stale, ETag.</summary>
            public static async Task<string> ConcurrencyAsync(string url)
            {
                using var http = new HttpClient { BaseAddress = new Uri(url) };
                var client = new FlagsClient(http);
                await client.PutFlag("checkout-v2", new NewFlag { Enabled = false, Description = "The new checkout" }, default);
                var etag = client.LastHeaders["ETag"];
                var first = await client.PatchFlag("checkout-v2", etag, new FlagPatch { Enabled = true, Rollout = 10 }, default);
                var changed = client.LastHeaders["ETag"] != etag;
                var second = await client.PatchFlag("checkout-v2", etag, new FlagPatch { Rollout = 50 }, default);
                var flag = (Flag)first.ToOneOf().Value;
                return $"v{flag.Version} {flag.Enabled} {flag.Rollout} {changed} | then {((PatchFlagError)second.ToOneOf().Value).Value.GetType().Name}";
            }
        }
        """;

    [Test]
    public async Task ETags_merge_patches_and_versions_behave_as_the_spec_and_overlay_say()
    {
        var (assembly, diagnostics) = GeneratedServer.Build("Flags", Path.Combine(Samples, "openapi.yaml"), [Path.Combine(Samples, "flags.overlay.yaml")], Source, withRules: true, resultful: true);
        diagnostics.Where(d => d.Severity >= Microsoft.CodeAnalysis.DiagnosticSeverity.Warning).Should().BeEmpty();
        var server = assembly.GetType("Flags.Api.Server")!;
        var app = await (Task<WebApplication>)server.GetMethod("StartAsync")!.Invoke(null, null)!;
        try
        {
            var url = app.Urls.Single() + "/";
            using var client = new HttpClient { BaseAddress = new Uri(url) };
            var spec = SpecJson.Normalize(File.ReadAllText(Path.Combine(Samples, "openapi.yaml")), "openapi.yaml", out _,
                [(File.ReadAllText(Path.Combine(Samples, "flags.overlay.yaml")), "flags.overlay.yaml")]);

            var report = await RegressionSuite.FromJson(spec).RunAsync(client, new RegressionOptions { Random = 5 });

            File.WriteAllText(Path.Combine(TestContext.CurrentContext.WorkDirectory, "flags.txt"), report.ToString());
            report.Failures.Should().BeEmpty(report.ToString());
            (await (Task<string>)server.GetMethod("ConcurrencyAsync")!.Invoke(null, [url])!).Should().Be("v2 True 10 True | then PatchFlagPreconditionFailed");

            // The twin, from the spec alone, measured against the generated server: both start empty.
            var twin = new SpecTwin(spec);
            using var fake = new HttpClient(twin) { BaseAddress = new Uri("http://twin/") };
            await using var fresh = (WebApplication)await (Task<WebApplication>)server.GetMethod("StartAsync")!.Invoke(null, null)!;
            using var real = new HttpClient { BaseAddress = new Uri(fresh.Urls.Single() + "/") };
            var suite = RegressionSuite.FromJson(spec);
            var fidelity = (await suite.RunAsync(fake)).Fidelity(await suite.RunAsync(real));
            File.WriteAllText(Path.Combine(TestContext.CurrentContext.WorkDirectory, "flags-fidelity.txt"), fidelity.ToString());
            fidelity.Score.Should().BeGreaterThan(0.8, fidelity.ToString());
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private static string ThisDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
