using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Matchmark.Testing;
using Microsoft.AspNetCore.Builder;
using NUnit.Framework;

namespace Matchmark.Generator.Tests;

/// <summary>
/// The fidelity loop with an agent in it (GAPS.md, gap 3), on the Ledger sample: its real server (LedgerTests) and a
/// twin from the spec alone, measured with the same suite from the same two accounts. The writer saw only the training
/// part's tasks (ledger-tasks.json); each behaviour below was kept because the held-out score rose and no operation
/// fell. LAYERS.md records the run.
/// </summary>
[TestFixture]
public sealed class AgentLoopTests
{
    private const int HeldOutPercent = 30;

    private static readonly System.Text.Json.JsonSerializerOptions Indented = new() { WriteIndented = true };

    private static readonly string Samples = Path.GetFullPath(Path.Combine(ThisDirectory(), "..", "samples", "ledger"));

    /// <summary>
    /// The Ledger's spec as the agents saw it, before traits: without x-matchmark-references (and unique keys), which now
    /// give the twin some of what the agents had to write. The runs recorded here stay as they happened.
    /// </summary>
    private static readonly string Spec = WithoutTraits(SpecJson.Normalize(File.ReadAllText(Path.Combine(Samples, "openapi.yaml")), "openapi.yaml", out _,
        [(File.ReadAllText(Path.Combine(Samples, "ledger.overlay.yaml")), "ledger.overlay.yaml")]));

    private static string WithoutTraits(string spec)
    {
        var document = System.Text.Json.Nodes.JsonNode.Parse(spec)!;
        foreach (var schema in document["components"]?["schemas"]?.AsObject().Select(p => p.Value).OfType<System.Text.Json.Nodes.JsonObject>() ?? [])
        {
            schema.Remove("x-matchmark-references");
            schema.Remove("x-matchmark-unique");
            schema.Remove("x-matchmark-derived");
        }

        return document.ToJsonString();
    }

    [Test]
    public async Task The_twin_without_owned_behaviour_is_measured_and_its_training_tasks_written()
    {
        var real = RegressionReport.Record(await RealAsync());
        var (training, heldOut) = FidelityLoop.Split(real, HeldOutPercent);
        var twin = await TwinAsync(_ => { });

        var seen = twin.Fidelity(training);
        var judged = twin.Fidelity(heldOut);
        File.WriteAllText(Path.Combine(TestContext.CurrentContext.WorkDirectory, "ledger-tasks.json"),
            System.Text.Json.JsonSerializer.Serialize(seen.Tasks, Indented));
        File.WriteAllText(Path.Combine(TestContext.CurrentContext.WorkDirectory, "ledger-loop.txt"),
            $"{seen}\nHeld out: {judged.Matched}/{judged.Steps} ({judged.Score * 100:0.#}%)\n");
        seen.Steps.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task Each_attempt_is_judged_on_sequences_its_writer_never_saw()
    {
        var real = RegressionReport.Record(await RealAsync());
        var (training, heldOut) = FidelityLoop.Split(real, HeldOutPercent);
        var log = new System.Text.StringBuilder();
        var kept = (await TwinAsync(_ => { })).Fidelity(heldOut);
        log.AppendLine(CultureInfo.InvariantCulture, $"baseline: held out {kept.Score:P1}");

        async Task<FidelityVerdict> Attempt(string name, Action<SpecTwin> behaviours)
        {
            var run = await TwinAsync(behaviours);
            var verdict = FidelityLoop.Judge(kept, run.Fidelity(heldOut));
            log.AppendLine(CultureInfo.InvariantCulture, $"{name}: training {run.Fidelity(training).Score:P1}, held out {run.Fidelity(heldOut).Score:P1}. {verdict}");
            foreach (var task in run.Fidelity(training).Tasks)
            {
                log.AppendLine(CultureInfo.InvariantCulture, $"  task left: {task.Subject} | {task.Difference} | {task.Status} vs {task.RealStatus}");
            }

            if (verdict.Keep)
            {
                kept = run.Fidelity(heldOut);
            }

            return verdict;
        }

        // 1. Accounts as the real ledger keeps them, in the behaviour's own store: getTransfer fell, so it's rejected.
        var first = await Attempt("attempt 1", twin => Accounts(twin, keepTwinStore: false));
        // 2. The same, with the twin's own store kept in step for what refers to accounts: kept.
        var second = await Attempt("attempt 2", twin => Accounts(twin, keepTwinStore: true));
        // 3. Plus the real ledger's empty 415: training reaches 100%, but nothing held out rises, so it's rejected.
        var third = await Attempt("attempt 3", twin => { Accounts(twin, keepTwinStore: true); Rejections(twin); });
        File.WriteAllText(Path.Combine(TestContext.CurrentContext.WorkDirectory, "ledger-gate.txt"), log.ToString());

        first.Keep.Should().BeFalse(log.ToString());
        first.Reasons.Should().Contain(r => r.StartsWith("getTransfer fell", StringComparison.Ordinal), log.ToString());
        second.Keep.Should().BeTrue(log.ToString());
        third.Keep.Should().BeFalse(log.ToString());
        kept.Score.Should().BeGreaterThan(0.8, log.ToString());
    }

    /// <summary>
    /// The loop's gate over <see cref="LedgerOwned"/>: writes the training tasks it leaves (ledger-owned.txt) and the
    /// verdict against the twin without it, and never the held-out mismatches.
    /// </summary>
    [Test]
    public async Task The_owned_behaviour_passes_the_gate()
    {
        var real = RegressionReport.Record(await RealAsync());
        var (training, heldOut) = FidelityLoop.Split(real, HeldOutPercent);
        var before = (await TwinAsync(_ => { })).Fidelity(heldOut);
        var run = await TwinAsync(LedgerOwned.Apply);
        var seen = run.Fidelity(training);
        var verdict = FidelityLoop.Judge(before, run.Fidelity(heldOut));
        File.WriteAllText(Path.Combine(TestContext.CurrentContext.WorkDirectory, "ledger-owned.txt"),
            $"Training: {seen.Matched}/{seen.Steps} ({seen.Score:P1}). {verdict}\n"
            + string.Join("\n", seen.Tasks.Select(t => $"TASK {t.Subject} | differs at: {t.Difference}\n  twin {t.Status}: {t.Body}\n  real {t.RealStatus}: {t.RealBody}")) + "\n");
        verdict.Keep.Should().BeTrue(verdict.ToString());
    }

    /// <summary>
    /// Attempts 1 and 2, from the training tasks: the real ledger keeps an account as its id, owner and balance; the
    /// opening amount isn't stored, it's the balance. Kept in the behaviour's own state, ids in the order accounts are
    /// opened, and (attempt 2) in the twin's, for what refers to accounts.
    /// </summary>
    private static void Accounts(SpecTwin twin, bool keepTwinStore)
    {
        var accounts = new System.Collections.Concurrent.ConcurrentDictionary<string, JsonObject>();
        var next = 0;
        twin.On("openAccount", call =>
        {
            var id = Interlocked.Increment(ref next);
            var account = new JsonObject { ["id"] = id, ["owner"] = call.Body?["owner"]?.DeepClone(), ["balance"] = call.Body?["opening"]?.DeepClone() };
            accounts[id.ToString(CultureInfo.InvariantCulture)] = account;
            if (keepTwinStore)
            {
                twin.Seed("/accounts", [account]);
            }

            return new TwinAnswer(201, account.DeepClone());
        });
        twin.On("getAccount", call => accounts.TryGetValue(call.Path["accountId"], out var account) ? new TwinAnswer(200, account.DeepClone()) : null);
    }

    /// <summary>Attempt 3, from the training tasks: the real ledger answers 415 with no body (ASP.NET Core's own refusal).</summary>
    private static void Rejections(SpecTwin twin)
        => twin.OnRejected(rejection => rejection.Status == 415 ? new TwinAnswer(415) : null);

    private static async Task<RegressionReport> RealAsync()
    {
        var (assembly, _) = GeneratedServer.Build("Ledger", Path.Combine(Samples, "openapi.yaml"), [Path.Combine(Samples, "ledger.overlay.yaml")], LedgerTests.Source, withRules: true);
        var app = await (Task<WebApplication>)assembly.GetType("Ledger.Api.Server")!.GetMethod("StartAsync")!.Invoke(null, null)!;
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single() + "/") };
            return await RunAsync(client);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private static async Task<RegressionReport> TwinAsync(Action<SpecTwin> behaviours)
    {
        var twin = new SpecTwin(Spec);
        behaviours(twin);
        using var client = new HttpClient(twin) { BaseAddress = new Uri("http://twin/") };
        return await RunAsync(client);
    }

    /// <summary>The suite from two accounts, as LedgerTests runs it.</summary>
    private static async Task<RegressionReport> RunAsync(HttpClient client)
    {
        await client.PostAsync("accounts", new StringContent("""{"owner":"Ada","opening":"500.00"}""", System.Text.Encoding.UTF8, "application/json"));
        await client.PostAsync("accounts", new StringContent("""{"owner":"Bo","opening":"0.00"}""", System.Text.Encoding.UTF8, "application/json"));
        return await RegressionSuite.FromJson(Spec).RunAsync(client, new RegressionOptions { MaxRetryWait = TimeSpan.Zero });
    }

    private static string ThisDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
