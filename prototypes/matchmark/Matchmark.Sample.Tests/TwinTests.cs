using System.Net.Http.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using NUnit.Framework;
using PetStore.Api;
using Matchmark.Testing;

namespace Matchmark.Sample.Tests;

/// <summary>
/// The twin (GAPS.md, gaps 1 and 2): a stateful fake of the Pet Store from its spec alone, held to the suite generated
/// from the same spec, then measured against the real service: the share of steps that answer the same.
/// </summary>
[TestFixture]
public sealed class TwinTests
{
    [Test]
    public async Task A_twin_from_the_spec_alone_passes_the_suite_but_for_behaviour_beyond_CRUD()
    {
        var twin = new SpecTwin(PetStoreRegression.Spec);
        using var client = new HttpClient(twin) { BaseAddress = new Uri("http://twin/") };

        var report = await PetStoreRegression.Suite.RunAsync(client);

        report.Workflows.Single(w => w.Name == "createPet 201, then getPet, upsertPet, deletePet").Exchanges.Select(e => $"{e.Request.Method} {e.Status}")
            .Should().Equal("POST 201", "GET 200", "PUT 200", "GET 200", "DELETE 204", "GET 404");
        report.Failures.Select(f => $"{f.Check} {f.Subject}").Should().Equal(
            $"{Checks.WorkflowCriteria} Pet Store: register-and-rename, step name-taken");
    }

    [Test]
    public async Task Its_fidelity_to_the_real_service_is_measured_step_by_step()
    {
        await using var app = new PetStoreApp();
        var real = app.CreateClient();
        var twin = new SpecTwin(PetStoreRegression.Spec);
        twin.Seed("/pets", (await real.GetFromJsonAsync<JsonArray>("pets?limit=100"))!.OfType<JsonObject>());
        using var fake = new HttpClient(twin) { BaseAddress = new Uri("http://twin/") };

        var expected = await PetStoreRegression.Suite.RunAsync(real);
        var actual = await PetStoreRegression.Suite.RunAsync(fake);
        var fidelity = actual.Fidelity(expected);

        File.WriteAllText(Path.Combine(TestContext.CurrentContext.WorkDirectory, "twin-fidelity.txt"), fidelity.ToString());
        actual.Fidelity(FidelityRecording.FromJson(RegressionReport.Record(expected).ToJson())).Should().BeEquivalentTo(fidelity,
            "a recording of the real API, replayed in a PR build, scores the same as the live run");
        fidelity.Score.Should().BeInRange(0.8, 0.99, fidelity.ToString());
        fidelity.Mismatches.Should().Contain(m => m.StartsWith("Pet Store: register-and-rename, step 5 (POST /pets): 201 here, 409 from the real API", StringComparison.Ordinal),
            "a unique name is behaviour the spec doesn't state, so the twin doesn't have it");

        // Owned behaviour (GAPS.md, gap 3): a name already taken is a conflict. It's kept because the score rises.
        var owned = await Measure(twin => twin
            .On("createPet", call => Taken(call, except: null))
            .On("upsertPet", call => Taken(call, except: call.Path["petId"])));
        File.WriteAllText(Path.Combine(TestContext.CurrentContext.WorkDirectory, "twin-fidelity-owned.txt"), owned.ToString());
        owned.Score.Should().BeGreaterThan(fidelity.Score, owned.ToString());
        owned.Mismatches.Should().NotContain(m => m.Contains("409 from the real API", StringComparison.Ordinal), owned.ToString());
    }

    [Test]
    public async Task A_change_is_kept_only_when_its_held_out_score_rises_and_no_operation_falls()
    {
        // GAPS.md, gap 3: the writer sees the training part's tasks; a change is judged on the held-out part.
        var (_, real) = await Run(_ => { });
        var recording = RegressionReport.Record(real);
        // Half held out, which puts the unique-name workflow (the 409s) on the held-out side.
        var (training, heldOut) = FidelityLoop.Split(recording, heldOutPercent: 50);
        training.Steps.Concat(heldOut.Steps).Should().BeEquivalentTo(recording.Steps, "the split loses and repeats nothing");
        training.Steps.Select(s => s.Subject).Intersect(heldOut.Steps.Select(s => s.Subject)).Should().BeEmpty();
        heldOut.Steps.Where(s => s.Operation == "Pet Store: register-and-rename").Should().HaveCount(
            recording.Steps.Count(s => s.Operation == "Pet Store: register-and-rename"), "a workflow is held out whole");
        FidelityLoop.Split(recording, heldOutPercent: 50).HeldOut.Should().BeEquivalentTo(heldOut, "the split is the same every run");
        FidelityLoop.Split(recording, heldOutPercent: 30).Training.Steps.Where(s => s.Operation == "Pet Store: register-and-rename").Should().HaveCount(
            recording.Steps.Count(s => s.Operation == "Pet Store: register-and-rename"), "at 30% it's trained on, also whole");

        var (twin, _) = await Run(_ => { });
        var seen = twin.Fidelity(training);
        seen.Tasks.Should().HaveCount(seen.Mismatches.Count).And.OnlyContain(t => training.Steps.Any(s => s.Subject == t.Subject), "the writer sees only training tasks");
        var before = twin.Fidelity(heldOut);
        before.Tasks.Should().Contain(t => t.Status == 201 && t.RealStatus == 409 && t.Difference == "status", before.ToString());

        // The unique-name rule closes the held-out 409s without lowering anything: kept.
        var (owned, _) = await Run(twin => twin
            .On("createPet", call => Taken(call, except: null))
            .On("upsertPet", call => Taken(call, except: call.Path["petId"])));
        var kept = FidelityLoop.Judge(before, owned.Fidelity(heldOut));
        kept.Keep.Should().BeTrue(kept.ToString());

        // Refusing every new pet passes the 409s too, but everything else about createPet falls: rejected.
        var (broken, _) = await Run(twin => twin.On("createPet", _ => new TwinAnswer(409, new JsonObject { ["title"] = "A pet with that name exists", ["status"] = 409 })));
        var rejected = FidelityLoop.Judge(before, broken.Fidelity(heldOut));
        rejected.Keep.Should().BeFalse(rejected.ToString());
        rejected.Reasons.Should().Contain(r => r.StartsWith("createPet fell", StringComparison.Ordinal), rejected.ToString());

        // Nothing changed is nothing gained.
        FidelityLoop.Judge(before, before).Keep.Should().BeFalse();
    }

    /// <summary>The Pet Store's unique names, as the real service has them: another pet with the name is a 409.</summary>
    private static TwinAnswer? Taken(TwinCall call, string? except)
        => call.Resources.GetValueOrDefault("/pets") is { } pets && call.Body?["name"]?.GetValue<string>() is { } name
           && pets.Any(p => p["name"]?.GetValue<string>() == name && p["id"]?.ToJsonString() != except)
            ? new TwinAnswer(409, new JsonObject { ["title"] = "A pet with that name exists", ["status"] = 409 })
            : null;

    private static async Task<FidelityReport> Measure(Action<SpecTwin> behaviours)
    {
        var (fake, real) = await Run(behaviours);
        return fake.Fidelity(real);
    }

    /// <summary>The suite against a twin with these behaviours, and against the real service, both from the same pets.</summary>
    private static async Task<(RegressionReport Twin, RegressionReport Real)> Run(Action<SpecTwin> behaviours)
    {
        await using var app = new PetStoreApp();
        var real = app.CreateClient();
        var twin = new SpecTwin(PetStoreRegression.Spec);
        behaviours(twin);
        twin.Seed("/pets", (await real.GetFromJsonAsync<JsonArray>("pets?limit=100"))!.OfType<JsonObject>());
        using var fake = new HttpClient(twin) { BaseAddress = new Uri("http://twin/") };
        var expected = await PetStoreRegression.Suite.RunAsync(real);
        return (await PetStoreRegression.Suite.RunAsync(fake), expected);
    }
}
