using System.Net;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Matchmark.Conformance;
using Matchmark.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;

namespace Matchmark.Generator.Tests;

/// <summary>
/// Runtime conformance: the engine's response checks on a server's own responses. A small hand-written API, nothing
/// generated, with three planted faults: a name longer than the schema allows, a 404 problem sent as plain JSON, and a
/// postcondition broken. Observed, they're reported and the responses go out unchanged; enforced, each becomes a 500.
/// </summary>
[TestFixture]
public sealed class ConformanceTests
{
    private const string Spec = """
        openapi: 3.1.0
        info: { title: Things, version: '1' }
        paths:
          /things/{thingId}:
            get:
              operationId: getThing
              parameters: [{ name: thingId, in: path, required: true, schema: { type: integer } }]
              x-matchmark-ensures:
                - condition: $response.body#/id == $request.path.thingId
              responses:
                '200':
                  description: the thing
                  content: { application/json: { schema: { $ref: '#/components/schemas/Thing' } } }
                '404':
                  description: no such thing
                  content: { application/problem+json: { schema: { type: object } } }
        components:
          schemas:
            Thing:
              type: object
              required: [id, name]
              properties:
                id: { type: integer }
                name: { type: string, maxLength: 5 }
        """;

    [Test]
    public async Task Observed_violations_are_reported_and_the_responses_go_out_unchanged()
    {
        var reported = new List<(string Path, IReadOnlyList<Finding> Findings)>();
        await using var app = await Start(o => o.OnViolation = (context, findings) => reported.Add((context.Request.Path.Value!, findings)));
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        (await client.GetAsync("/things/1")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/things/2")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/things/3")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/things/4")).StatusCode.Should().Be(HttpStatusCode.OK);

        reported.Select(r => (r.Path, Checks: string.Join(",", r.Findings.Select(f => f.Check).Distinct()))).Should().Equal(
            ("/things/2", Checks.BodyConforms),
            ("/things/3", Checks.ContentTypeDocumented),
            ("/things/4", Checks.Postcondition));
    }

    [Test]
    public async Task Enforced_a_violating_response_becomes_a_500_naming_it()
    {
        await using var app = await Start(o => o.Mode = ConformanceMode.Enforce);
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        var fine = await client.GetAsync("/things/1");
        var broken = await client.GetAsync("/things/2");

        fine.StatusCode.Should().Be(HttpStatusCode.OK);
        (await fine.Content.ReadAsStringAsync()).Should().Be("""{"id":1,"name":"Rex"}""");
        broken.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        broken.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        JsonNode.Parse(await broken.Content.ReadAsStringAsync())!["detail"]!.GetValue<string>()
            .Should().Contain("[BodyConforms] getThing").And.Contain("name");
    }

    private static async Task<WebApplication> Start(Action<ConformanceOptions> configure)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        app.UseMatchmarkConformance(SpecJson.Normalize(Spec, "things.yaml"), configure);
        app.MapGet("/things/{thingId:int}", (int thingId) => thingId switch
        {
            1 => Results.Json(new { id = 1, name = "Rex" }),
            2 => Results.Json(new { id = 2, name = "Rex the second" }),
            3 => Results.Json(new { title = "No such thing" }, statusCode: 404),
            _ => Results.Json(new { id = 99, name = "Max" }),
        });
        await app.StartAsync();
        return app;
    }
}
