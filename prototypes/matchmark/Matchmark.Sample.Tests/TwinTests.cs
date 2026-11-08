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

    /// <summary>The Pet Store's unique names, as the real service has them: another pet with the name is a 409.</summary>
    private static TwinAnswer? Taken(TwinCall call, string? except)
        => call.Resources.GetValueOrDefault("/pets") is { } pets && call.Body?["name"]?.GetValue<string>() is { } name
           && pets.Any(p => p["name"]?.GetValue<string>() == name && p["id"]?.ToJsonString() != except)
            ? new TwinAnswer(409, new JsonObject { ["title"] = "A pet with that name exists", ["status"] = 409 })
            : null;

    private static async Task<FidelityReport> Measure(Action<SpecTwin> behaviours)
    {
        await using var app = new PetStoreApp();
        var real = app.CreateClient();
        var twin = new SpecTwin(PetStoreRegression.Spec);
        behaviours(twin);
        twin.Seed("/pets", (await real.GetFromJsonAsync<JsonArray>("pets?limit=100"))!.OfType<JsonObject>());
        using var fake = new HttpClient(twin) { BaseAddress = new Uri("http://twin/") };
        var expected = await PetStoreRegression.Suite.RunAsync(real);
        return (await PetStoreRegression.Suite.RunAsync(fake)).Fidelity(expected);
    }
}
