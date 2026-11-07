using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;
using Matchmark.Cli;

namespace Matchmark.Generator.Tests;

/// <summary>
/// <c>matchmark regress</c>: the regression suite generated from a spec, run over plain HTTP against an API nothing here
/// generated. A hand-written Todo API, with one bug planted, on a real port.
/// </summary>
[TestFixture]
public sealed class RegressCommandTests
{
    private const string Spec = """
        openapi: 3.1.0
        info: { title: Todos, version: '1' }
        paths:
          /todos:
            post:
              operationId: addTodo
              requestBody:
                required: true
                content: { application/json: { schema: { $ref: '#/components/schemas/NewTodo' } } }
              responses:
                '201':
                  description: added
                  content: { application/json: { schema: { $ref: '#/components/schemas/Todo' } } }
                  links:
                    GetTodo: { operationId: getTodo, parameters: { id: $response.body#/id } }
                '400':
                  description: invalid
                  content: { application/problem+json: { schema: { $ref: '#/components/schemas/Problem' } } }
          /todos/{id}:
            get:
              operationId: getTodo
              parameters: [{ name: id, in: path, required: true, schema: { type: integer, format: int32, minimum: 1 } }]
              responses:
                '200': { description: the todo, content: { application/json: { schema: { $ref: '#/components/schemas/Todo' } } } }
                '400': { description: invalid, content: { application/problem+json: { schema: { $ref: '#/components/schemas/Problem' } } } }
                '404': { description: no such todo }
        components:
          schemas:
            NewTodo: { type: object, required: [title], properties: { title: { type: string, minLength: 1, maxLength: 20 } } }
            Todo: { type: object, required: [id, title], properties: { id: { type: integer, format: int32 }, title: { type: string } } }
            Problem: { type: object, properties: { title: { type: string }, errors: { type: object } } }
        """;

    [Test]
    public async Task A_planted_bug_fails_the_run_and_is_named()
    {
        var (exitCode, output) = await Run(acceptsLongTitles: true);

        exitCode.Should().Be(1, output);
        output.Should().Contain("Failures of InvalidRejected (1):")
            .And.Contain("addTodo: body.title longer than its maxLength 20: an invalid request was accepted with 201")
            .And.Contain("curl -i -X POST");
    }

    [Test]
    public async Task The_fixed_api_passes()
    {
        var (exitCode, output) = await Run(acceptsLongTitles: false);

        exitCode.Should().Be(0, output);
        output.Should().Contain("0 failures").And.Contain("Workflow addTodo 201, then getTodo: POST 201 → GET 200");
    }

    [Test]
    public async Task An_Arazzo_workflow_given_with_the_spec_runs_too()
    {
        const string workflow = """
            arazzo: 1.1.0
            info: { title: Todos, version: '1' }
            sourceDescriptions: [{ name: todos, url: ./todos.yaml, type: openapi }]
            workflows:
              - workflowId: add-and-read
                steps:
                  - stepId: add
                    operationId: addTodo
                    requestBody: { contentType: application/json, payload: { title: Buy milk } }
                    successCriteria: [{ condition: $statusCode == 201 }]
                    outputs: { id: $response.body#/id }
                  - stepId: read
                    operationId: getTodo
                    parameters: [{ name: id, in: path, value: $steps.add.outputs.id }]
                    successCriteria: [{ condition: $response.body#/title == 'Buy milk' }]
            """;

        var (exitCode, output) = await Run(acceptsLongTitles: false, workflow);

        exitCode.Should().Be(0, output);
        output.Should().Contain("1 from Arazzo").And.Contain("Workflow Todos: add-and-read: POST 201 → GET 200");
    }

    private static async Task<(int ExitCode, string Output)> Run(bool acceptsLongTitles, string? arazzo = null)
    {
        var spec = Path.Combine(Path.GetTempPath(), $"todos-{Guid.NewGuid():N}.yaml");
        var workflows = Path.ChangeExtension(spec, ".arazzo.yaml");
        await File.WriteAllTextAsync(spec, Spec);
        await File.WriteAllTextAsync(workflows, arazzo ?? string.Empty);
        await using var api = TodoApi(acceptsLongTitles);
        await api.StartAsync();
        try
        {
            using var output = new StringWriter();
            var exitCode = await Regress.RunAsync(arazzo is null ? [spec, api.Urls.Single()] : [spec, api.Urls.Single(), "--arazzo", workflows], output);
            return (exitCode, output.ToString());
        }
        finally
        {
            await api.StopAsync();
            File.Delete(spec);
            File.Delete(workflows);
        }
    }

    /// <summary>A Todo API written by hand, reading JSON itself: the regression engine knows nothing about it but the spec.</summary>
    private static WebApplication TodoApi(bool acceptsLongTitles)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        var todos = new Dictionary<int, string>();

        static IResult Invalid(string key, string message) => Results.ValidationProblem(new Dictionary<string, string[]> { [key] = [message] });

        app.MapPost("/todos", async (HttpRequest request) =>
        {
            if (request.ContentType?.StartsWith("application/json", StringComparison.Ordinal) != true)
            {
                return Invalid("body", "Send JSON.");
            }

            JsonNode? body;
            try
            {
                body = await JsonNode.ParseAsync(request.Body);
            }
            catch (System.Text.Json.JsonException)
            {
                return Invalid("body", "Not JSON.");
            }

            if (body is not JsonObject todo || todo["title"] is not JsonValue value || !value.TryGetValue<string>(out var title))
            {
                return Invalid("title", "A title is required.");
            }

            if (title.Length < 1 || (title.Length > 20 && !acceptsLongTitles))
            {
                return Invalid("title", "1 to 20 characters.");
            }

            var id = todos.Count + 1;
            todos[id] = title;
            return Results.Json(new { id, title }, statusCode: 201);
        });

        app.MapGet("/todos/{id}", (string id) =>
            !int.TryParse(id, out var n) || n < 1 ? Invalid("id", "A positive whole number.")
            : todos.TryGetValue(n, out var title) ? Results.Json(new { id = n, title })
            : Results.NotFound());
        return app;
    }
}
