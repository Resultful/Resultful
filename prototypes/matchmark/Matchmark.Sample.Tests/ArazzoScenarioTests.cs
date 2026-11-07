using System.Text.Json.Nodes;
using AwesomeAssertions;
using NUnit.Framework;
using PetStore.Api;
using PetStore.Fake;
using Matchmark.Fakes;

namespace Matchmark.Sample.Tests;

/// <summary>
/// The Arazzo workflow the regression suite runs against the service, scripting the fake a consumer is tested against:
/// a client that follows <c>register-and-rename</c> passes; one that strays from it fails at the step it strays.
/// </summary>
[TestFixture]
public sealed class ArazzoScenarioTests
{
    private static Scenario Workflow() => Scenario.FromArazzo(PetStoreRegression.Workflows.Single(), "register-and-rename", new JsonObject { ["name"] = "Rex" });

    [Test]
    public void The_workflow_becomes_its_steps_in_order()
        => Workflow().Steps.Select(s => s.ToString()).Should().Equal(
            """createPet → 201 expecting {"body":{"name":"Rex","details":{"kind":"cat","livesLeft":9}}}""",
            """getPet → 200 expecting {"petId":"$steps.register.outputs.petId"}""",
            "listPets → 200",
            """upsertPet → 200 expecting {"petId":"$steps.register.outputs.petId","body":{"name":"Rex-renamed","details":{"kind":"cat","livesLeft":8}}}""",
            """createPet → 409 expecting {"body":{"name":"$steps.rename.outputs.name","details":{"kind":"dog","goodBoy":true}}}""",
            """deletePet → 204 expecting {"petId":"$steps.register.outputs.petId"}""",
            """getPet → 404 expecting {"petId":"$steps.register.outputs.petId"}""");

    [Test]
    public async Task A_client_that_follows_the_workflow_passes()
    {
        var fake = new PetStoreFake().UseScenario(Workflow());

        var failures = await Follow(fake, renamedTo: "Rex-renamed", useTheAnsweredId: true);

        failures.Should().BeEmpty();
        fake.Verify().Should().BeEquivalentTo(new { Passed = true, Failures = Array.Empty<string>() });
    }

    [Test]
    public async Task A_client_that_reads_another_pet_than_it_created_fails_at_that_step()
    {
        var fake = new PetStoreFake().UseScenario(Workflow());

        var failures = await Follow(fake, renamedTo: "Rex-renamed", useTheAnsweredId: false);

        failures[0].Should().StartWith("step 2 (getPet → 200 expecting {\"petId\":\"$steps.register.outputs.petId\"}): petId expected");
        fake.Verify().Passed.Should().BeFalse();
    }

    [Test]
    public async Task A_client_that_sends_another_body_fails_at_the_field()
    {
        var fake = new PetStoreFake().UseScenario(Workflow());

        var failures = await Follow(fake, renamedTo: "Rex 2", useTheAnsweredId: true);

        failures[0].Should().Contain("body.name expected \"Rex-renamed\", got \"Rex 2\"");
    }

    [Test]
    public void A_workflow_that_branches_can_not_script_a_fake()
    {
        const string arazzo = """
            {"arazzo":"1.1.0","info":{"title":"t","version":"1"},"sourceDescriptions":[],"workflows":[{"workflowId":"w","steps":[
              {"stepId":"a","operationId":"listPets","onSuccess":[{"name":"again","type":"goto","stepId":"a"}]}]}]}
            """;

        var act = () => Scenario.FromArazzo(arazzo, "w");

        act.Should().Throw<ArgumentException>().WithMessage("Step 'a' goes to another step on success*");
    }

    /// <summary>A consumer of the Pet Store following the workflow; returns what the fake refused.</summary>
    private static async Task<List<string>> Follow(PetStoreFake fake, string renamedTo, bool useTheAnsweredId)
    {
        var failures = new List<string>();
        async Task<T?> Call<T>(Func<Task<T>> call)
        {
            try
            {
                return await call();
            }
            catch (FakeExpectationException e)
            {
                failures.Add(e.Message);
                return default;
            }
        }

        var created = await Call(() => fake.CreatePet(new NewPet { Name = "Rex", Details = new Cat { LivesLeft = 9 } }, default));
        var id = created!.ToOneOf().Value is Pet pet ? pet.Id : 0;
        var read = useTheAnsweredId ? id : id + 1;
        await Call(() => fake.GetPet(read, default));
        await Call(() => fake.ListPets(null, default));
        var renamed = await Call(() => fake.UpsertPet(id, new NewPet { Name = renamedTo, Details = new Cat { LivesLeft = 8 } }, default));
        var name = renamed.ToOneOf().Value is UpsertPetSuccess success
            ? success.Value switch { UpsertPetOk ok => ok.Body.Name, UpsertPetCreated created2 => created2.Body.Name, _ => null }
            : null;
        await Call(() => fake.CreatePet(new NewPet { Name = name ?? renamedTo, Details = new Dog { GoodBoy = true } }, default));
        await Call(async () => { await fake.DeletePet(id, default); return 0; });
        await Call(() => fake.GetPet(id, default));
        return failures;
    }
}
