using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Matchmark.Conformance;
using Matchmark.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using PetStore.Api;
using PetStore.Data;

namespace Matchmark.Sample.Tests;

/// <summary>
/// Hosted sessions with two kinds of copy, on the Pet Store: the built-in twin (an in-memory store inferred from the
/// spec) and the real service, whose sessions each get an ephemeral SQLite database through EF Core
/// (<see cref="SessionDatabases"/>), created and seeded when the session starts and gone when it ends.
/// </summary>
[TestFixture]
public sealed class SessionCopyTests
{
    private SessionsApp _service = null!;
    private SessionDatabases _databases = null!;
    private WebApplication _host = null!;
    private HttpClient _control = null!;

    [SetUp]
    public async Task Start()
    {
        _service = new SessionsApp();
        var serviceClient = _service.CreateClient();
        _databases = _service.Services.GetRequiredService<SessionDatabases>();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _host = builder.Build();
        _host.UseMatchmarkSessions(new SessionHost(PetStoreRegression.Spec, PetStoreRegression.Workflows)
        {
            Service = new ServiceCopy(serviceClient,
                (id, seed, cancellationToken) => _databases.StartAsync(id, seed?.GetValue<string>(), cancellationToken),
                id =>
                {
                    _databases.End(id);
                    return Task.CompletedTask;
                }),
        });
        await _host.StartAsync();
        _control = new HttpClient { BaseAddress = new Uri(_host.Urls.Single() + "/") };
    }

    [TearDown]
    public async Task Stop()
    {
        _control.Dispose();
        await _host.StopAsync();
        await _host.DisposeAsync();
        await _service.DisposeAsync();
    }

    [Test]
    public async Task Each_service_session_has_its_own_ephemeral_database()
    {
        await using var demo = await Session("demo", seed: "Demo");
        await using var empty = await Session("empty", seed: "Empty");

        (await Names(demo.Client)).Should().Equal("Rex", "Tom", "Bella");
        (await Names(empty.Client)).Should().BeEmpty();
        (await empty.Client.PostAsJsonAsync("pets", Dog("Max"))).StatusCode.Should().Be(HttpStatusCode.Created);
        (await Names(empty.Client)).Should().Equal("Max");
        (await Names(demo.Client)).Should().Equal(["Rex", "Tom", "Bella"], "the other session's database is untouched");
        _databases.Open.Should().BeEquivalentTo(["default", "demo", "empty"]);

        // Ending the session discards its database; starting the id again gives a fresh one.
        (await _control.DeleteAsync("__fake/sessions/empty")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        _databases.Open.Should().NotContain("empty");
        await using var again = await Session("empty", seed: "Empty");
        (await Names(again.Client)).Should().BeEmpty("Max lived only in the ended session's database");
    }

    [Test]
    public async Task The_suite_the_spec_writes_passes_through_a_service_session()
    {
        await using var session = await Session("regress", seed: "Empty");

        var report = await PetStoreRegression.Suite.RunAsync(session.Client);

        report.Failures.Should().BeEmpty(report.ToString());
        report.Cases.Count.Should().BeGreaterThan(50);
        _databases.Open.Should().Contain("regress");
    }

    [Test]
    public async Task A_script_answers_before_the_service_does()
    {
        await using var session = await Session("scripted", seed: "Empty");
        await session.GivenAsync("createPet", 409, new { title = "Name taken", status = 409 }, times: 1);

        (await session.Client.PostAsJsonAsync("pets", Dog("Rex"))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await session.Client.PostAsJsonAsync("pets", Dog("Rex"))).StatusCode.Should().Be(HttpStatusCode.Created, "the script was for one call; then the service answers");
        (await Names(session.Client)).Should().Equal("Rex");
    }

    [Test]
    public async Task The_same_pathway_shows_where_the_built_in_store_stops_and_the_service_goes_on()
    {
        // The Pet Store's pathway ends by registering a name that's taken: a 409 only the real service knows about.
        await using var service = await Session("service-path", seed: "Empty", pathways: ["register-and-rename"]);
        await using var twin = await MatchmarkSession.StartAsync(_control, "twin-path", pathways: ["register-and-rename"]);

        var options = new RegressionOptions { Include = _ => false, MaxRetryWait = TimeSpan.Zero };
        await PetStoreRegression.Suite.RunAsync(service.Client, options);
        await PetStoreRegression.Suite.RunAsync(twin.Client, options);

        var onService = await service.ReportAsync();
        var onTwin = await twin.ReportAsync();
        onService.Consumer.Pathways.Single().Completed.Should().BeTrue(string.Join("; ", onService.Reasons));
        onTwin.Consumer.Pathways.Single().Missing.Should().Equal(["name-taken", "remove", "gone"],
            "the twin infers CRUD from the spec, and a unique name is behaviour the spec doesn't state; the workflow stops at that step");
    }

    private Task<MatchmarkSession> Session(string id, string seed, IEnumerable<string>? pathways = null)
        => MatchmarkSession.StartAsync(_control, id, seed: seed, pathways: pathways, copy: SessionCopy.Service);

    private static NewPet Dog(string name) => new() { Name = name, Details = new Dog { GoodBoy = true } };

    private static async Task<List<string>> Names(HttpClient client)
        => (await client.GetFromJsonAsync<JsonArray>("pets"))!.Select(p => p!["name"]!.GetValue<string>()).ToList();

    /// <summary>The Pet Store in sessions mode: a database per session, and one for requests with none.</summary>
    private sealed class SessionsApp : WebApplicationFactory<PetStore.PetsHandlers>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("PetStore:Sessions", "true");
            builder.UseSetting("PetStore:Seed", "Empty");
        }
    }
}
