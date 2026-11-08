using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Matchmark.Conformance;
using Matchmark.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using NUnit.Framework;

namespace Matchmark.Generator.Tests;

/// <summary>
/// Hosted sessions (docs/reference/sessions.md), on the Shelter sample: a copy of the API per session, started by the
/// consumer with an id it chooses, routed by host, header or path, guarded by a token in the spec's own scheme, scripted
/// with documented outcomes only, and reported from both directions against the published pathways.
/// </summary>
[TestFixture]
public sealed class SessionTests
{
    private static readonly string Samples = Path.GetFullPath(Path.Combine(ThisDirectory(), "..", "samples", "sessions"));

    private static readonly string Spec = SpecJson.Normalize(File.ReadAllText(Path.Combine(Samples, "openapi.yaml")), "openapi.yaml");

    private static readonly string Pathways = SpecJson.Arazzo(File.ReadAllText(Path.Combine(Samples, "shelter.arazzo.yaml")), "shelter.arazzo.yaml");

    private WebApplication _app = null!;
    private SessionHost _host = null!;
    private HttpClient _control = null!;

    [SetUp]
    public async Task Start()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _app = builder.Build();
        _host = _app.UseMatchmarkSessions(new SessionHost(Spec, [Pathways]));
        await _app.StartAsync();
        _control = new HttpClient { BaseAddress = new Uri(_app.Urls.Single() + "/") };
    }

    [TearDown]
    public async Task Stop()
    {
        _control.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    [Test]
    public async Task Each_session_is_its_own_copy_reached_by_host_header_or_path()
    {
        await using var one = await MatchmarkSession.StartAsync(_control, "test1", seed: new Dictionary<string, object> { ["/pets"] = new[] { new { id = 1, name = "Tom" } } });
        await using var two = await MatchmarkSession.StartAsync(_control, "test2");
        one.BaseUrl.Should().MatchRegex(@"^http://test1\.localhost:\d+/$", "an IP address has no subdomains, so a session's own host is under localhost");

        (await one.Client.PostAsJsonAsync("pets", new { name = "Rex" })).StatusCode.Should().Be(HttpStatusCode.Created);
        (await Names(one.Client, "pets")).Should().Equal("Tom", "Rex");
        (await Names(two.Client, "pets")).Should().BeEmpty("a session sees only its own state");

        // By host: the Host header, not DNS, picks the copy.
        using var byHost = new HttpRequestMessage(HttpMethod.Get, "pets");
        byHost.Headers.Host = "test1.localhost";
        using var hosted = await _control.SendAsync(byHost);
        (await hosted.Content.ReadFromJsonAsync<JsonArray>())!.Should().HaveCount(2);

        // By path prefix, as the prototype's fake hub did.
        (await Names(_control, "sessions/test1/pets")).Should().Equal("Tom", "Rex");

        // No session, or one nobody started: a 404 problem, never an empty copy.
        (await _control.GetAsync("pets")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var unknown = new HttpRequestMessage(HttpMethod.Get, "pets");
        unknown.Headers.Add("X-Matchmark-Session", "test9");
        var missing = await _control.SendAsync(unknown);
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await missing.Content.ReadAsStringAsync()).Should().Contain("There is no session 'test9'");
    }

    [Test]
    public async Task A_token_belongs_to_its_session_and_public_operations_need_none()
    {
        await using var one = await MatchmarkSession.StartAsync(_control, "test1");
        await using var two = await MatchmarkSession.StartAsync(_control, "test2");

        // Without a token: a secured operation is 401, a public one answers.
        using var anonymous = new HttpClient { BaseAddress = _control.BaseAddress };
        anonymous.DefaultRequestHeaders.Add("X-Matchmark-Session", "test1");
        (await anonymous.PostAsJsonAsync("pets", new { name = "Rex" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync("pets")).StatusCode.Should().Be(HttpStatusCode.OK, "listPets is security: []");

        // Another session's token is refused, by name.
        var twoToken = two.Client.DefaultRequestHeaders.Authorization!.ToString();
        using var crossed = new HttpRequestMessage(HttpMethod.Post, "pets") { Content = JsonContent.Create(new { name = "Rex" }) };
        crossed.Headers.Add("X-Matchmark-Session", "test1");
        crossed.Headers.TryAddWithoutValidation("Authorization", twoToken);
        var refused = await _control.SendAsync(crossed);
        refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("This token belongs to session 'test2'");

        (await one.Client.PostAsJsonAsync("pets", new { name = "Rex" })).StatusCode.Should().Be(HttpStatusCode.Created, "its own token is accepted");
    }

    [Test]
    public async Task Scripted_answers_are_documented_outcomes_only_and_a_restart_starts_clean()
    {
        await using var one = await MatchmarkSession.StartAsync(_control, "test1");
        await one.GivenAsync("createPet", 409, new { title = "A pet with that name exists", status = 409 }, times: 1);

        (await one.Client.PostAsJsonAsync("pets", new { name = "Rex" })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await one.Client.PostAsJsonAsync("pets", new { name = "Rex" })).StatusCode.Should().Be(HttpStatusCode.Created, "the script was for one call");
        await FluentActions.Awaiting(() => one.GivenAsync("createPet", 418)).Should().ThrowAsync<InvalidOperationException>().WithMessage("*doesn't document a 418*");

        // Starting the same id again replaces the session: 200, and nothing left from before.
        (await _control.PutAsJsonAsync("__fake/sessions/test1", new { })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Names(one.Client, "pets")).Should().BeEmpty();
        (await _control.PutAsJsonAsync("__fake/sessions/Test_1", new { })).StatusCode.Should().Be(HttpStatusCode.BadRequest, "an id must be a host label");
    }

    [Test]
    public async Task The_report_is_complete_only_when_the_consumer_covered_its_pathways_and_the_provider_passes_them()
    {
        // The provider's half: the same pathways run by the engine against the provider (here, a session of its own).
        await using (var provider = await MatchmarkSession.StartAsync(_control, "provider"))
        {
            var suite = RegressionSuite.FromJson(Spec, Pathways);
            var run = await suite.RunAsync(provider.Client, new RegressionOptions { Include = _ => false, MaxRetryWait = TimeSpan.Zero });
            run.Failures.Should().BeEmpty(run.ToString());
            _host.Provider = ProviderResults.FromJson(ProviderResults.Of(run).ToJson());
        }

        await using var covered = await MatchmarkSession.StartAsync(_control, "covered", pathways: ["register-a-pet", "browse"]);
        var created = await (await covered.Client.PostAsJsonAsync("pets", new { name = "Rex" })).Content.ReadFromJsonAsync<JsonObject>();
        await covered.Client.GetAsync($"pets/{created!["id"]}");
        await covered.Client.GetAsync("pets");

        var report = await covered.ReportAsync();
        report.Consumer.Pathways.Should().OnlyContain(p => p.Completed, report.ToString());
        report.Provider!.Passed.Should().BeTrue();
        report.Complete.Should().BeTrue();

        await using var partial = await MatchmarkSession.StartAsync(_control, "partial", pathways: ["all"]);
        await partial.Client.PostAsJsonAsync("pets", new { name = "Rex" });
        await partial.Client.GetAsync("pets/999");
        var incomplete = await partial.ReportAsync();
        incomplete.Consumer.Pathways.Single(p => p.WorkflowId == "register-a-pet").Missing.Should().Equal("read");
        incomplete.Consumer.Pathways.Single(p => p.WorkflowId == "browse").Missing.Should().Equal("list");
        incomplete.Complete.Should().BeFalse();
        incomplete.Reasons.Should().Contain("the consumer didn't complete register-a-pet: missing read");
        report.Reasons.Should().BeEmpty();
    }

    private static async Task<List<string>> Names(HttpClient client, string path)
        => (await client.GetFromJsonAsync<JsonArray>(path))!.Select(p => p!["name"]!.GetValue<string>()).ToList();

    private static string ThisDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
