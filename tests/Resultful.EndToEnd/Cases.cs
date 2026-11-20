using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Resultful.EndToEnd;

internal sealed record E2ECase(string Ledger, string Name, Func<Task<CaseResult>> Run);

internal sealed record BrokenConsumer(string Name, string Ledger, string Force, bool NeedsGenerator, bool NeedsAnalyzers, string Core = "package");

// Every end-to-end case, by the ledger line it belongs to.
internal static class Cases
{
    private static readonly string[] Targets = ["net10.0", "net11.0"];

    // Broken consumers (VERIFY.md section 3): each must fail to build with exactly Broken/<Name>/expected.txt.
    // A consumer that needs the generator or the analyzers isn't built until that package is packed.
    public static readonly BrokenConsumer[] Broken =
    [
        // Needs the analyzers: without the suppressor CS8509 fires on every switch over a closed base, so it proves nothing.
        new("MissingArm", "RF-SUPPRESS", "UC01", NeedsGenerator: true, NeedsAnalyzers: true),
        new("DiscardedResult", "RF-0001", "UC01", NeedsGenerator: true, NeedsAnalyzers: true),
        new("DefaultResult", "RF-0002", "UC01", NeedsGenerator: true, NeedsAnalyzers: true),
        new("ForeignDerived", "RF-0010", "UC01", NeedsGenerator: true, NeedsAnalyzers: true),
        new("BareString", "RF-0004", "", NeedsGenerator: false, NeedsAnalyzers: true),
        new("UntypedMatch", "RF-0008", "UC01", NeedsGenerator: true, NeedsAnalyzers: true),
        new("ReorderedMatch", "HIER-MATCH", "UC01", NeedsGenerator: true, NeedsAnalyzers: false),
        new("OwnMustObserve", "UC09", "UC09", NeedsGenerator: false, NeedsAnalyzers: true, Core: "none"),
    ];

    public static IEnumerable<E2ECase> All()
    {
        // Each use case compiles from the packed packages, alone (ShopForce=UC0n), with the generator's real output.
        foreach (var (uc, projects) in new (string, string[])[]
        {
            ("UC01", ["Domain", "Api", "Net11"]),
            ("UC02", ["Domain", "Api"]),
            ("UC03", ["Domain", "Api"]),
            ("UC04", ["Domain", "Api"]),
            ("UC05", ["Domain", "Api", "Net11"]),
            ("UC06", ["Domain", "Worker"]),
            ("UC07", ["Api"]),
            ("UC08", ["Api"]),
            ("UC10", ["Api"]),
        })
        {
            foreach (var project in projects)
            {
                yield return new(uc, $"Shop.{project} builds", async () => Built($"Shop.{project} builds", await ShopBuild(project, uc)));
            }
        }

        foreach (var c in HttpCases.Load())
        {
            yield return new("HTTP-CASES", $"HTTP {c.Method} {c.Path} → {c.Status} ({c.Leaf ?? "success"}, {c.UseCase})", async () => (await ApiRun(c.UseCase))[c.Key]);
            // The OpenAPI document lists the success and each failure status of the route.
            yield return new("HTTP-OPENAPI", $"OpenAPI {c.Method} {c.Route} lists {c.Status}", async () => (await ApiRun(c.UseCase))["openapi " + c.Key]);
        }

        yield return new("UC06", "Shop.Worker's dispositions", WorkerRun);
        yield return new("UC08", "Shop.Tests passes", ShopTests);
        yield return new("UC09", "Shop.Legacy's library builds with no Resultful reference", async () =>
            Built("UC09 library", await Build(ShopProject("Legacy"), "UC09", ["-p:ShopCore=none"])));
        // The migration's compiler errors are 12's (MIG-LEGACY); UC10's line keeps the "after" code.
        yield return new("MIG-LEGACY", "Shop.Legacy builds against 0.2", LegacyAgainstBaseline);
        yield return new("MIG-LEGACY", "Shop.Legacy fails against 0.3 with UC10/expected-0.3.txt", LegacyAgainstTarget);

        foreach (var b in Broken)
        {
            yield return new(b.Ledger, $"Broken/{b.Name} fails with its expected IDs", () => BrokenRun(b));
        }
    }

    private static string ShopProject(string name) => Repo.Path("samples", $"Resultful.Shop.{name}", $"Resultful.Shop.{name}.csproj");

    private static CaseResult Built(string name, BuildResult build) =>
        new(name, build.Process.Succeeded, build.Process.Succeeded ? "built" : "build failed:\n" + build.Process.Tail(25));

    internal sealed record BuildResult(ProcessResult Process, string Artifacts);

    private static readonly ConcurrentDictionary<string, Lazy<Task<BuildResult>>> Builds = new(StringComparer.Ordinal);

    private static Task<BuildResult> ShopBuild(string project, string force) => Build(ShopProject(project), force, []);

    // One build per (project, forced use cases, extra properties), cached for the run, each in its own artifacts path.
    internal static Task<BuildResult> Build(string project, string force, IReadOnlyList<string> extra, bool fromFeed = true) =>
        Builds.GetOrAdd($"{project}|{force}|{string.Join(' ', extra)}|{fromFeed}", key => new Lazy<Task<BuildResult>>(async () =>
        {
            var artifacts = Path.Combine(Repo.RunDir, "build", $"{Path.GetFileNameWithoutExtension(project)}-{force.Replace(';', '-')}-{(uint)key.GetHashCode(StringComparison.Ordinal):x8}");
            List<string> args = ["build", project, "-c", "Release", "--artifacts-path", artifacts, "-m:2", $"-p:ShopForce={force.Replace(";", "%3B", StringComparison.Ordinal)}"];
            if (fromFeed)
            {
                var feed = await Feed.Get();
                args.AddRange(feed.BuildProperties());
            }

            args.AddRange(extra);
            return new BuildResult(await Run.Dotnet(args), artifacts);
        })).Value;

    private static string? FindOutput(string artifacts, string assembly, string tfm) =>
        Directory.Exists(Path.Combine(artifacts, "bin"))
            ? Directory.GetFiles(Path.Combine(artifacts, "bin"), assembly + ".dll", SearchOption.AllDirectories)
                .FirstOrDefault(f => f.Contains($"_{tfm}", StringComparison.Ordinal))
            : null;

    private static readonly ConcurrentDictionary<string, Lazy<Task<Dictionary<string, CaseResult>>>> ApiRuns = new(StringComparer.Ordinal);

    // Shop.Api built for one use case, started on each target in Production, every HTTP case of that use case sent.
    private static Task<Dictionary<string, CaseResult>> ApiRun(string uc) => ApiRuns.GetOrAdd(uc, _ => new(async () =>
    {
        var cases = HttpCases.Load().Where(c => c.UseCase == uc).ToList();
        var results = new Dictionary<string, CaseResult>(StringComparer.Ordinal);
        var build = await ShopBuild("Api", uc);
        if (!build.Process.Succeeded)
        {
            foreach (var c in cases)
            {
                results[c.Key] = new(c.Key, false, "Shop.Api didn't build:\n" + build.Process.Tail(15));
                results["openapi " + c.Key] = new(c.Key, false, "Shop.Api didn't build");
            }

            return results;
        }

        var failures = cases.ToDictionary(c => c.Key, _ => new List<string>(), StringComparer.Ordinal);
        var openApiFailures = cases.ToDictionary(c => c.Key, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var tfm in Targets)
        {
            var dll = FindOutput(build.Artifacts, "Resultful.Shop.Api", tfm);
            if (dll is null)
            {
                foreach (var c in cases) failures[c.Key].Add($"{tfm}: no Resultful.Shop.Api.dll");
                continue;
            }

            await using var api = await RunningApi.Start(dll, HttpCases.Environment);
            if (api.StartupFailure is { } why)
            {
                foreach (var c in cases) failures[c.Key].Add($"{tfm}: {why}");
                continue;
            }

            var openApi = await api.GetJson("/openapi/v1.json");
            foreach (var c in cases)
            {
                if (await c.Check(api.Client) is { } problem) failures[c.Key].Add($"{tfm}: {problem}");
                if (HttpCases.OpenApiMismatch(openApi, c) is { } missing) openApiFailures[c.Key].Add($"{tfm}: {missing}");
            }
        }

        foreach (var c in cases)
        {
            results[c.Key] = new(c.Key, failures[c.Key].Count == 0, string.Join("; ", failures[c.Key]));
            results["openapi " + c.Key] = new(c.Key, openApiFailures[c.Key].Count == 0 && failures[c.Key].Count == 0, string.Join("; ", openApiFailures[c.Key].Concat(failures[c.Key])));
        }

        return results;
    })).Value;

    private static async Task<CaseResult> WorkerRun()
    {
        const string name = "Shop.Worker's dispositions";
        var build = await ShopBuild("Worker", "UC06");
        if (!build.Process.Succeeded) return Built(name, build);
        var expected = File.ReadAllText(Repo.Path("samples", "Resultful.Shop.Worker", "UC06", "expected-output.txt")).ReplaceLineEndings("\n").Trim();
        var problems = new List<string>();
        foreach (var tfm in Targets)
        {
            var dll = FindOutput(build.Artifacts, "Resultful.Shop.Worker", tfm);
            if (dll is null) { problems.Add($"{tfm}: no Resultful.Shop.Worker.dll"); continue; }
            var run = await Run.Dotnet([dll], timeout: TimeSpan.FromMinutes(1));
            var actual = run.Output.ReplaceLineEndings("\n").Trim();
            if (!run.Succeeded || actual != expected) problems.Add($"{tfm}: exit {run.ExitCode}, output:\n{actual}\nexpected:\n{expected}");
        }

        return new(name, problems.Count == 0, string.Join("\n", problems));
    }

    private static async Task<CaseResult> ShopTests()
    {
        var feed = await Feed.Get();
        var artifacts = Path.Combine(Repo.RunDir, "build", "Shop.Tests-UC08");
        var run = await Run.Dotnet(["test", ShopProject("Tests"), "-c", "Release", "--artifacts-path", artifacts, "-p:ShopForce=UC08", .. feed.BuildProperties()]);
        return new("Shop.Tests passes", run.Succeeded, run.Succeeded ? "passed" : run.Tail(25));
    }

    private static async Task<CaseResult> LegacyAgainstBaseline()
    {
        // 0.2 comes from nuget.org, not the feed: no source mapping, but still an isolated package folder.
        var build = await Build(ShopProject("Legacy"), "UC10", ["-p:ShopCore=baseline", "-p:ShopLedger=none", $"-p:RestorePackagesPath={Path.Combine(Repo.RunDir, "packages-baseline")}", "-p:TreatWarningsAsErrors=true"], fromFeed: false);
        return Built("Shop.Legacy builds against 0.2", build);
    }

    private static async Task<CaseResult> LegacyAgainstTarget()
    {
        const string name = "Shop.Legacy against 0.3";
        var sarif = Path.Combine(Repo.RunDir, "sarif", "Legacy-UC10.sarif");
        Directory.CreateDirectory(Path.GetDirectoryName(sarif)!);
        // Compiler errors only: the analyzers' findings on this code (RF0004 from 16b) aren't part of the comparison.
        var build = await Build(ShopProject("Legacy"), "UC10", ["-f", "net10.0", $"-p:ErrorLog={sarif}%2Cversion=2.1", "-p:ShopAnalyzers=false"]);
        return CompareSarif(name, build.Process, sarif, Repo.Path("samples", "Resultful.Shop.Legacy", "UC10", "expected-0.3.txt"));
    }

    private static async Task<CaseResult> BrokenRun(BrokenConsumer b)
    {
        var name = $"Broken/{b.Name}";
        var feed = await Feed.Get();
        if (b.NeedsGenerator && !feed.Has("Resultful.Generators")) return new(name, false, "needs Resultful.Generators, which isn't packed yet");
        if (b.NeedsAnalyzers && !feed.Has("Resultful.Analyzers")) return new(name, false, "needs Resultful.Analyzers, which isn't packed yet");
        var dir = Repo.Path("tests", "Resultful.EndToEnd", "Broken", b.Name);
        var sarif = Path.Combine(Repo.RunDir, "sarif", b.Name + ".sarif");
        Directory.CreateDirectory(Path.GetDirectoryName(sarif)!);
        List<string> extra = [$"-p:BrokenSarif={sarif}"];
        if (b.Core != "package") extra.Add($"-p:ShopCore={b.Core}");
        var build = await Build(Path.Combine(dir, b.Name + ".csproj"), b.Force, extra);
        return CompareSarif(name, build.Process, sarif, Path.Combine(dir, "expected.txt"));
    }

    internal static CaseResult CompareSarif(string name, ProcessResult build, string sarif, string expectedFile)
    {
        if (build.Succeeded) return new(name, false, "built, but must not");
        if (!File.Exists(sarif)) return new(name, false, "no SARIF log (the build failed before compiling):\n" + build.Tail(15));
        var mismatch = Sarif.Compare(Sarif.ReadExpected(expectedFile), Sarif.ErrorIds(sarif));
        return new(name, mismatch is null, mismatch ?? "fails as expected");
    }
}

// http-cases.json.
internal sealed record HttpCase(string UseCase, string? Leaf, string Method, string Path, string Route, int Status, JsonNode? Request, bool Problem, JsonNode? Body, string[] Absent, string[] MustNotContain)
{
    public string Key => $"{Method} {Path} {Status} {Leaf}";

    public async Task<string?> Check(HttpClient client)
    {
        using var request = new HttpRequestMessage(new HttpMethod(Method), Path);
        if (Request is not null) request.Content = new StringContent(Request.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        if ((int)response.StatusCode != Status) return $"status {(int)response.StatusCode}, expected {Status}: {text}";
        if (Problem && response.Content.Headers.ContentType?.MediaType != "application/problem+json") return $"content type {response.Content.Headers.ContentType}, expected application/problem+json";
        JsonNode? body = null;
        if (text.Length > 0)
        {
            try { body = JsonNode.Parse(text); }
            catch (JsonException) { return $"body isn't JSON: {text}"; }
        }

        if (Body is not null && JsonSubset.Mismatch(Body, body) is { } m) return $"{m} in {text}";
        foreach (var key in Absent) if (JsonSubset.Has(body, key)) return $"{key} must be absent: {text}";
        foreach (var word in MustNotContain) if (text.Contains(word, StringComparison.OrdinalIgnoreCase)) return $"body leaks \"{word}\": {text}";
        return null;
    }
}

internal static class HttpCases
{
    private static readonly Lazy<(string Environment, List<HttpCase> Cases)> Data = new(Read);

    public static string Environment => Data.Value.Environment;

    public static List<HttpCase> Load() => Data.Value.Cases;

    private static (string, List<HttpCase>) Read()
    {
        var root = JsonNode.Parse(File.ReadAllText(Repo.Path("tests", "Resultful.EndToEnd", "http-cases.json")))!;
        var cases = root["cases"]!.AsArray().Select(c => new HttpCase(
            (string)c!["useCase"]!,
            (string?)c["leaf"],
            (string)c["method"]!,
            (string)c["path"]!,
            (string)c["route"]!,
            (int)c["status"]!,
            c["request"]?.DeepClone(),
            (bool?)c["problem"] ?? false,
            c["body"]?.DeepClone(),
            c["absent"]?.AsArray().Select(a => (string)a!).ToArray() ?? [],
            c["mustNotContain"]?.AsArray().Select(a => (string)a!).ToArray() ?? [])).ToList();
        return ((string)root["environment"]!, cases);
    }

    public static string? OpenApiMismatch(JsonNode? document, HttpCase c)
    {
        if (document is null) return "no OpenAPI document";
        var responses = document["paths"]?[c.Route]?[c.Method.ToLowerInvariant()]?["responses"] as JsonObject;
        if (responses is null) return $"{c.Method} {c.Route} isn't in the OpenAPI document";
        return responses.ContainsKey(c.Status.ToString(System.Globalization.CultureInfo.InvariantCulture))
            ? null
            : $"{c.Method} {c.Route} lists [{string.Join(", ", responses.Select(r => r.Key))}], not {c.Status}";
    }
}

// The Shop's API as a child process on a free local port.
internal sealed class RunningApi : IAsyncDisposable
{
    private readonly System.Diagnostics.Process _process;
    private readonly StringBuilder _output;

    private RunningApi(System.Diagnostics.Process process, StringBuilder output, HttpClient client)
    {
        _process = process;
        _output = output;
        Client = client;
    }

    public HttpClient Client { get; }
    public string? StartupFailure { get; private set; }

    public static async Task<RunningApi> Start(string dll, string environment)
    {
        int port;
        using (var probe = new TcpListener(IPAddress.Loopback, 0))
        {
            probe.Start();
            port = ((IPEndPoint)probe.LocalEndpoint).Port;
        }

        var output = new StringBuilder();
        var process = Run.Start("dotnet", [dll], new Dictionary<string, string>
        {
            ["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}",
            ["ASPNETCORE_ENVIRONMENT"] = environment,
            ["DOTNET_NOLOGO"] = "1",
        }, output);
        var api = new RunningApi(process, output, new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(30) });
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (process.HasExited)
            {
                api.StartupFailure = $"exited with {process.ExitCode}:\n{output}";
                return api;
            }

            try
            {
                using var ping = await api.Client.GetAsync("/openapi/v1.json");
                return api;
            }
            catch (HttpRequestException)
            {
                await Task.Delay(200);
            }
        }

        api.StartupFailure = "didn't answer within 20 seconds:\n" + output;
        return api;
    }

    public async Task<JsonNode?> GetJson(string path)
    {
        using var response = await Client.GetAsync(path);
        return response.IsSuccessStatusCode ? JsonNode.Parse(await response.Content.ReadAsStringAsync()) : null;
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
        }

        _process.Dispose();
    }
}
