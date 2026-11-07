using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using PetStore.Api;
using PetStore.Data;
using PetStore.Scaffolded;
using Matchmark.Testing;
using GeneratedSuite = Matchmark.Testing.RegressionSuite;

namespace Matchmark.Sample.Tests;

/// <summary>
/// The regression suite generated from the spec (<see cref="PetStoreRegression"/>, by the Regression part), knowing
/// nothing about how any server is built: run against the service, the untouched scaffold and the fake; run against two
/// implementations to compare them; and proven, check by check, to catch the regression it guards against.
/// </summary>
[TestFixture]
public sealed class SpecRegressionTests
{
    private static GeneratedSuite Suite => PetStoreRegression.Suite;

    [Test]
    public async Task The_service_passes_the_suite_generated_from_the_spec()
    {
        await using var app = new PetStoreApp();

        var report = await Suite.RunAsync(app.CreateClient());

        Write("service", report);
        report.Failures.Should().BeEmpty(report.ToString());
        report.Workflows.Should().ContainSingle().Which.Skipped.Should().BeNull("the workflow from createPet's links must run to the end");
        report.Outcomes.Where(o => !o.Reached).Should().BeEmpty("the suite reaches every documented outcome, conflicts and missing pets included");
    }

    [Test]
    public async Task The_untouched_scaffold_passes_the_suite()
    {
        // The reference implementation answers with the spec's examples and keeps nothing, so there is no state to follow.
        await using var server = new WebApplicationFactory<PetsController>();

        var report = await Suite.RunAsync(server.CreateClient(), new RegressionOptions { Workflows = false });

        Write("scaffold", report);
        report.Failures.Should().BeEmpty(report.ToString());
    }

    [Test]
    public async Task The_fake_server_passes_the_suite()
    {
        await using var server = await PetStoreFakeServer.StartAsync();
        using var client = new HttpClient { BaseAddress = server.BaseAddress };

        var report = await Suite.RunAsync(client, new RegressionOptions { Workflows = false });

        Write("fake", report);
        report.Failures.Should().BeEmpty(report.ToString());
    }

    [Test]
    public async Task Generated_endpoints_and_scaffolded_controllers_answer_identically_over_the_same_handlers()
    {
        // The two directions from one spec: the generated Minimal API endpoints, and the [ApiController] controllers a team
        // owns, each over the real handlers and an empty database. Every case and workflow step must get the same answer.
        await using var generated = new PetStoreApp();
        await using var scaffolded = new ScaffoldedService();
        await scaffolded.Services.EnsurePetStoreDatabaseAsync();

        var expected = await Suite.RunAsync(generated.CreateClient());
        var actual = await Suite.RunAsync(scaffolded.CreateClient());

        Write("scaffolded-service", actual);
        actual.Failures.Should().BeEmpty(actual.ToString());
        string.Join("\n", actual.Compare(expected)).Should().BeEmpty();
    }

    [TestCaseSource(nameof(Faults))]
    public async Task Each_check_catches_the_regression_it_guards_against(string check, Fault fault)
    {
        await using var app = new PetStoreApp();
        using var client = app.CreateDefaultClient(new FaultInjector(fault));

        var report = await Suite.RunAsync(client);

        report.Failures.Select(f => f.Check).Should().Contain(check, $"a server that {fault} must fail {check}");
    }

    [Test]
    public void The_suite_covers_every_operation_and_every_constraint()
    {
        File.WriteAllLines(Path.Combine(TestContext.CurrentContext.WorkDirectory, "regression-cases.txt"),
            Suite.Cases.Select(c => $"{c.Kind,-8} {c.Name}  →  {c.Request.Method} /{c.Request.Target} {c.Request.Body}"));
        Suite.Gaps.Should().BeEmpty();
        Suite.Spec.Operations.Select(o => o.Id).Should().BeSubsetOf(Suite.Cases.Select(c => c.Operation.Id));
        Suite.Cases.Select(c => c.Name).Should().OnlyHaveUniqueItems();

        // Every constraint in the spec is broken by some case, and every bound is met by another.
        var invalid = Suite.Cases.Where(c => c.Kind == CaseKind.Invalid).Select(c => c.Name).ToList();
        invalid.Should().Contain([
            "createPet: body.name shorter than its minLength 1",
            "createPet: body.name longer than its maxLength 50",
            "createPet: body.tag '!!', which doesn't match its pattern ^[a-z-]+$",
            "createPet: body.details.livesLeft below its minimum 0",
            "createPet: body.details.livesLeft above its maximum 9",
            "createPet: body.details.kind not one of 'dog', 'cat'",
            "createPet: body.details.kind missing",
            "createPet: body.name missing",
            "createPet: body not JSON",
            "getPet: path.petId a string, not integer",
        ]);
        Suite.Cases.Where(c => c.Kind == CaseKind.Boundary).Select(c => c.Name).Should().Contain([
            "createPet: body.name at its minLength 1",
            "createPet: body.name at its maxLength 50",
            "createPet: body.details.livesLeft at its minimum 0",
            "createPet: body.details.livesLeft at its maximum 9",
        ]);

        // A boundary sends exactly the bound, and every union member is sent.
        Suite.Case("createPet: body.name at its minLength 1").Request.Body.Should().Contain("\"name\":\"s\"");
        Suite.Case("createPet: valid, every value (cat)").Request.Body.Should().Contain("\"kind\":\"cat\"");
    }

    [Test]
    public void The_suite_is_the_same_every_time_it_is_generated()
    {
        var again = GeneratedSuite.FromJson(PetStoreRegression.Spec);

        again.Cases.Select(c => $"{c.Name} {c.Request.Curl(null)}")
            .Should().Equal(Suite.Cases.Select(c => $"{c.Name} {c.Request.Curl(null)}"));
    }

    private static void Write(string target, RegressionReport report)
        => File.WriteAllText(Path.Combine(TestContext.CurrentContext.WorkDirectory, $"regression-{target}.txt"), $"{Suite}\n{report}");

    // ---------------------------------------------------------------- faults

    /// <param name="Also">A request the faulty server also makes, on its own, after answering this one.</param>
    public sealed record Fault(
        string Description,
        Func<HttpRequestMessage, HttpResponseMessage?, Task<HttpResponseMessage?>> Apply,
        Func<HttpRequestMessage, HttpResponseMessage, HttpRequestMessage?>? Also = null)
    {
        public override string ToString() => Description;
    }

    private static IEnumerable<TestCaseData> Faults()
    {
        static TestCaseData Case(string check, string description, Func<HttpRequestMessage, HttpResponseMessage?, Task<HttpResponseMessage?>> apply,
            Func<HttpRequestMessage, HttpResponseMessage, HttpRequestMessage?>? also = null)
            => new TestCaseData(check, new Fault(description, apply, also)).SetArgDisplayNames(check);

        yield return Case(Checks.NotAServerError, "fails with 500 where it should say 404",
            (_, r) => Task.FromResult(r?.StatusCode == HttpStatusCode.NotFound ? Problem(500) : r));
        yield return Case(Checks.StatusDocumented, "says 410 Gone where the spec says 404",
            (_, r) => Task.FromResult(r?.StatusCode == HttpStatusCode.NotFound ? Problem(410) : r));
        yield return Case(Checks.ContentTypeDocumented, "sends JSON as text/plain",
            async (_, r) => r?.StatusCode == HttpStatusCode.OK ? Text(r.StatusCode, await r.Content.ReadAsStringAsync(), "text/plain") : r);
        yield return Case(Checks.BodyConforms, "leaves the status out of a pet",
            async (_, r) => r?.StatusCode == HttpStatusCode.OK ? Edit(r.StatusCode, await r.Content.ReadAsStringAsync(), json => Pets(json).ForEach(p => p.Remove("status"))) : r);
        yield return Case(Checks.ValidAccepted, "rejects a valid new pet",
            (_, r) => Task.FromResult(r?.StatusCode == HttpStatusCode.Created ? Problem(400) : r));
        yield return Case(Checks.InvalidRejected, "accepts what it should reject",
            async (_, r) => r?.StatusCode == HttpStatusCode.BadRequest ? Text(HttpStatusCode.Created, Rex, "application/json") : r);
        yield return Case(Checks.RejectionNamesTheValue, "says something was wrong, but not what",
            async (_, r) => r?.StatusCode == HttpStatusCode.BadRequest
                ? Edit(r.StatusCode, await r.Content.ReadAsStringAsync(), json => json["errors"] = new JsonObject { ["request"] = new JsonArray("Invalid.") }, "application/problem+json")
                : r);
        yield return Case(Checks.ResourceAvailable, "loses a pet as soon as it is created",
            (q, r) => Task.FromResult(q.Method == HttpMethod.Get && r?.StatusCode == HttpStatusCode.OK && q.RequestUri!.AbsolutePath.StartsWith("/pets/", StringComparison.Ordinal) ? Problem(404) : r));
        yield return Case(Checks.ReadAfterWrite, "reads back a different name than it wrote",
            async (q, r) => q.Method == HttpMethod.Get && r?.StatusCode == HttpStatusCode.OK && q.RequestUri!.AbsolutePath.StartsWith("/pets/", StringComparison.Ordinal)
                ? Edit(r.StatusCode, await r.Content.ReadAsStringAsync(), json => json["name"] = "Someone else")
                : r);
        yield return Case(Checks.DeletedIsGone, "says it deleted a pet, but didn't",
            (q, r) => Task.FromResult(q.Method == HttpMethod.Delete ? new HttpResponseMessage(HttpStatusCode.NoContent) : r));
        yield return Case(Checks.SafeChangesNothing, "deletes a pet whenever it is read",
            (_, r) => Task.FromResult(r),
            (q, r) => q.Method == HttpMethod.Get && r.StatusCode == HttpStatusCode.OK && q.RequestUri!.AbsolutePath.StartsWith("/pets/", StringComparison.Ordinal)
                ? new HttpRequestMessage(HttpMethod.Delete, q.RequestUri)
                : null);

        var puts = new HashSet<string>(StringComparer.Ordinal);
        yield return Case(Checks.IdempotentTwice, "renames a pet when the same PUT comes again",
            async (q, r) =>
            {
                if (r is null && q.Method == HttpMethod.Put && q.Content is { } content)
                {
                    var body = await content.ReadAsStringAsync();
                    if (!puts.Add($"{q.RequestUri} {body}") && JsonNode.Parse(body) is JsonObject pet)
                    {
                        pet["name"] = $"{pet["name"]}x";
                        q.Content = new StringContent(pet.ToJsonString(), Encoding.UTF8, "application/json");
                    }
                }

                return r;
            });
    }

    private const string Rex = """{"id":1,"name":"Rex","status":"available","details":{"kind":"dog","goodBoy":true}}""";

    private static HttpResponseMessage Problem(int status)
        => Text((HttpStatusCode)status, $$"""{"title":"Injected","status":{{status}}}""", "application/problem+json");

    private static HttpResponseMessage Text(HttpStatusCode status, string body, string mediaType)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

    private static HttpResponseMessage Edit(HttpStatusCode status, string body, Action<JsonNode> edit, string mediaType = "application/json")
    {
        var json = JsonNode.Parse(body)!;
        edit(json);
        return Text(status, json.ToJsonString(), mediaType);
    }

    private static List<JsonObject> Pets(JsonNode json)
        => json is JsonArray array ? array.OfType<JsonObject>().ToList() : json is JsonObject pet ? [pet] : [];

    /// <summary>Changes the server's answers (or answers instead of it, when the fault returns a response without asking).</summary>
    private sealed class FaultInjector(Fault fault) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // A fault that answers without the server (a delete that doesn't happen) is given no response to change.
            if (await fault.Apply(request, null) is { } instead)
            {
                return instead;
            }

            var response = await base.SendAsync(request, cancellationToken);
            if (fault.Also?.Invoke(request, response) is { } also)
            {
                (await base.SendAsync(also, cancellationToken)).Dispose();
            }

            return await fault.Apply(request, response) ?? response;
        }
    }

    /// <summary>The scaffolded controllers over the real handlers and database, in place of the reference implementation.</summary>
    private sealed class ScaffoldedService : WebApplicationFactory<PetsController>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:PetStore", $"Data Source=petstore-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
            builder.UseSetting("PetStore:Seed", "Empty");
            builder.ConfigureServices(services =>
            {
                services.AddPetStoreDatabase();
                services.AddScoped<PetStore.PetRepository>();
                services.AddScoped<IPetsHandlers, PetStore.PetsHandlers>();
            });
        }
    }
}
