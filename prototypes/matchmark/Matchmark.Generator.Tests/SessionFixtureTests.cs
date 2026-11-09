using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Matchmark.Conformance;
using Matchmark.Generator;
using Matchmark.Testing;
using Matchmark.Testing.NUnit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using NUnit.Framework;

namespace Matchmark.Generator.Tests;

/// <summary>The NUnit wrapper (Matchmark.Testing.NUnit): each test gets its own session, named after it, seeded and checked.</summary>
[TestFixture]
public sealed class SessionFixtureTests : MatchmarkSessionFixture
{
    private static readonly string Samples = Path.GetFullPath(Path.Combine(ThisDirectory(), "..", "samples", "sessions"));
    private static WebApplication _app = null!;
    private static HttpClient _control = null!;

    protected override HttpClient Control => _control;

    protected override object? Seed => new Dictionary<string, object> { ["/pets"] = new[] { new { id = 1, name = "Tom" } } };

    protected override Matchmark.Fakes.Scenario? Scenario => Matchmark.Fakes.Scenario.FromJson("""
        { "order": "AnyOrder", "steps": [ { "operation": "createPet", "expect": { "body": { "name": "Rex" } }, "status": 201 }, { "operation": "listPets", "status": 200 } ] }
        """);

    [OneTimeSetUp]
    public static async Task StartHost()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _app = builder.Build();
        _app.UseMatchmarkSessions(new SessionHost(SpecJson.Normalize(File.ReadAllText(Path.Combine(Samples, "openapi.yaml")), "openapi.yaml")));
        await _app.StartAsync();
        _control = new HttpClient { BaseAddress = new Uri(_app.Urls.Single() + "/") };
    }

    [OneTimeTearDown]
    public static async Task StopHost()
    {
        _control.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    [Test]
    public async Task Each_test_has_a_seeded_session_named_after_it()
    {
        Session.Id.Should().StartWith("matchmark-generator-tests-sessionfixturetests-");
        (await Session.Client.PostAsJsonAsync("pets", new { name = "Rex" })).StatusCode.Should().Be(HttpStatusCode.Created);
        (await Session.Client.GetFromJsonAsync<List<Dictionary<string, object>>>("pets"))!.Should().HaveCount(2, "Tom was seeded for this test");
        var scenario = (await Session.ReportAsync()).Consumer.Scenario!;
        scenario.Passed.Should().BeTrue(string.Join("; ", scenario.Failures));
    }

    [Test]
    public async Task Another_test_starts_from_the_same_seed()
        => (await Session.Client.GetFromJsonAsync<List<Dictionary<string, object>>>("pets"))!.Should().ContainSingle("the other test's pet was in its own session");

    [TestCase("Simple")]
    [TestCase("9 starts with a digit and goes on far longer than any host label is allowed to be, with (punctuation) too")]
    public void Ids_are_host_labels(string name)
        => MatchmarkSession.IdFor(name).Should().MatchRegex("^[a-z][a-z0-9-]{0,62}$");

    private static string ThisDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
