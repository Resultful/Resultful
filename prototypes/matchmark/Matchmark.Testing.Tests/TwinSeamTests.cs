using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using NUnit.Framework;

namespace Matchmark.Testing.Tests;

/// <summary>
/// The twin's seam for owned behaviour (GAPS.md, gap 3), narrowed to declared effects: a behaviour gets copies of the
/// request body and the stored resources, so changing them changes nothing; it changes the twin only by what its answer
/// declares, and the twin checks each declared effect before applying it.
/// </summary>
[TestFixture]
public sealed class TwinSeamTests
{
    private const string Spec = """
        {
          "openapi": "3.1.0",
          "info": { "title": "Notes", "version": "1" },
          "paths": {
            "/notes": {
              "post": {
                "operationId": "createNote",
                "requestBody": { "required": true, "content": { "application/json": { "schema": { "$ref": "#/components/schemas/NewNote" } } } },
                "responses": { "201": { "description": "created", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Note" } } } } }
              }
            },
            "/notes/{noteId}": {
              "parameters": [ { "name": "noteId", "in": "path", "required": true, "schema": { "type": "integer" } } ],
              "get": {
                "operationId": "getNote",
                "responses": {
                  "200": { "description": "the note", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Note" } } } },
                  "404": { "description": "none" }
                }
              }
            }
          },
          "components": {
            "schemas": {
              "NewNote": { "type": "object", "required": ["text"], "properties": { "text": { "type": "string", "maxLength": 20 } } },
              "Note": { "type": "object", "required": ["id", "text"], "properties": { "id": { "type": "integer" }, "text": { "type": "string", "maxLength": 20 } } }
            }
          }
        }
        """;

    private static async Task<(HttpClient Client, SpecTwin Twin)> Twin(Action<SpecTwin> owned)
    {
        var twin = new SpecTwin(Spec);
        owned(twin);
        var client = new HttpClient(twin) { BaseAddress = new Uri("http://twin/") };
        (await client.PostAsJsonAsync(new Uri("notes", UriKind.Relative), new { text = "first" })).StatusCode.Should().Be(HttpStatusCode.Created);
        return (client, twin);
    }

    private static async Task<string?> Text(HttpClient client)
        => JsonNode.Parse(await client.GetStringAsync(new Uri("notes/1", UriKind.Relative)))!["text"]?.GetValue<string>();

    [Test]
    public async Task Changing_what_a_behaviour_is_given_changes_nothing()
    {
        var (client, _) = await Twin(twin => twin.On("getNote", call =>
        {
            foreach (var note in call.Resources["/notes"])
            {
                note["text"] = "changed in place";
            }

            return null;
        }));

        (await Text(client)).Should().Be("first", "the behaviour was given copies");
    }

    [Test]
    public async Task A_declared_effect_changes_what_the_twin_stores()
    {
        var (client, _) = await Twin(twin => twin.On("getNote", call =>
        {
            var note = call.Resources["/notes"].Single();
            return note["text"]?.GetValue<string>() == "first"
                ? TwinAnswer.Continue(effects: TwinEffect.Replace("/notes", note["id"]!.ToString(), new JsonObject { ["id"] = note["id"]!.DeepClone(), ["text"] = "edited" }))
                : null;
        }));

        (await Text(client)).Should().Be("edited");
    }

    [Test]
    public async Task An_effect_that_breaks_the_schema_is_refused_and_nothing_is_applied()
    {
        var (client, twin) = await Twin(twin => twin.On("getNote", call =>
            TwinAnswer.Continue(effects: TwinEffect.Replace("/notes", "1", new JsonObject { ["id"] = 1, ["text"] = new string('x', 21) }))));

        var response = await client.GetAsync(new Uri("notes/1", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await response.Content.ReadAsStringAsync()).Should().Contain("breaks the schema");
        twin.Resources["/notes"].Single()["text"]!.GetValue<string>().Should().Be("first");
    }

    [Test]
    public async Task An_effect_on_a_resource_that_isn_t_there_is_refused()
    {
        var (client, _) = await Twin(twin => twin.On("getNote", _ => TwinAnswer.Continue(effects: TwinEffect.Remove("/notes", "99"))));

        var response = await client.GetAsync(new Uri("notes/1", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await response.Content.ReadAsStringAsync()).Should().Contain("/notes has no 99");
    }

    [Test]
    public async Task A_rewritten_body_is_what_the_twin_stores()
    {
        var (client, _) = await Twin(twin => twin.On("createNote", call =>
            call.Body is JsonObject body && body["text"]?.GetValue<string>() == "first" ? TwinAnswer.Continue(new JsonObject { ["text"] = "FIRST" }) : null));

        (await Text(client)).Should().Be("FIRST");
    }
}
