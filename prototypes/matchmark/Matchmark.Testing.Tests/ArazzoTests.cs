using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using NUnit.Framework;

namespace Matchmark.Testing.Tests;

/// <summary>Arazzo in the engine: the criteria grammar, runtime expressions, control flow, and what isn't supported.</summary>
[TestFixture]
public sealed class ArazzoTests
{
    private static ArazzoContext Context(string body, int status = 200)
    {
        var request = new RegressionRequest("POST", "/things/7", [new("q", "x")], [new("If-Match", "\"v1\"")], """{"a":1}""", "application/json")
        {
            PathParameters = new Dictionary<string, string> { ["id"] = "7" },
        };
        var context = new ArazzoContext(new JsonObject { ["name"] = "Rex", ["n"] = 3 })
        {
            Current = new Exchange(request, status, "application/json", body, new Dictionary<string, string> { ["ETag"] = "\"v2\"" }),
        };
        context.Steps["create"] = new JsonObject { ["id"] = 42 };
        return context;
    }

    [TestCase("$statusCode == 200", true)]
    [TestCase("$statusCode != 200 || $response.body#/name == 'Rex'", true)]
    [TestCase("$statusCode >= 200 && $statusCode < 300", true)]
    [TestCase("!($statusCode == 404)", true)]
    [TestCase("$response.body#/items/1/price > 9.5", true)]
    [TestCase("$response.body#/name == $inputs.name", true)]
    [TestCase("$steps.create.outputs.id == 42", true)]
    [TestCase("$response.header.ETag == '\"v2\"'", true)]
    [TestCase("$request.path.id == 7", true)]
    [TestCase("$request.header.If-Match == '\"v1\"'", true)]
    [TestCase("$response.body#/missing == null", true)]
    [TestCase("$response.body#/tags/0 == 'a' && $inputs.n == 3", true)]
    [TestCase("$response.body#/name == 'Tom'", false)]
    [TestCase("$inputs.n > 3", false)]
    public void Simple_conditions(string condition, bool holds)
    {
        var context = Context("""{"name":"Rex","tags":["a","b"],"items":[{"price":5},{"price":10}]}""");

        Criteria.Holds(new JsonObject { ["condition"] = condition }, context).Should().Be(holds);
    }

    [TestCase("simple", "$.items[1].price == 10", true)]
    [TestCase("simple", "$.name == 'Rex'", true)]
    [TestCase("regex", "^R[a-z]+$", true)]
    [TestCase("jsonpath", "$.items[?@.price > 7]", true)]
    [TestCase("jsonpath", "$.items[?(@.price > 70)]", false)]
    [TestCase("jsonpath", "$.tags[*]", true)]
    [TestCase("jsonpath", "$['name']", true)]
    [TestCase("jsonpath", "$.nothing", false)]
    public void Criteria_with_a_context(string type, string condition, bool holds)
    {
        var context = Context("""{"name":"Rex","tags":["a","b"],"items":[{"price":5},{"price":10}]}""");
        var criterion = new JsonObject
        {
            ["context"] = type == "regex" ? "$response.body#/name" : "$response.body",
            ["condition"] = condition,
            ["type"] = type,
        };

        Criteria.Holds(criterion, context).Should().Be(holds);
    }

    [Test]
    public void Expressions_are_substituted_in_payloads()
    {
        var context = Context("""{"name":"Rex"}""");

        context.Substitute(JsonNode.Parse("""{"id":"$steps.create.outputs.id","label":"{$inputs.name}-{$inputs.n}","fixed":"plain","list":["$inputs.name"]}"""))!
            .ToJsonString().Should().Be("""{"id":42,"label":"Rex-3","fixed":"plain","list":["Rex"]}""");
    }

    // ---------------------------------------------------------------- control flow, against a counter

    private const string Spec = """
        {
          "openapi": "3.1.0", "info": { "title": "Counter", "version": "1" },
          "paths": {
            "/counter": {
              "get": { "operationId": "read", "responses": { "200": { "description": "ok", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Count" } } } } } },
              "post": { "operationId": "bump", "responses": { "200": { "description": "ok", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Count" } } } } } }
            }
          },
          "components": { "schemas": { "Count": { "type": "object", "required": ["n"], "properties": { "n": { "type": "integer" } } } } }
        }
        """;

    private static string Workflow(string steps) => $$"""
        {
          "arazzo": "1.1.0", "info": { "title": "Counter", "version": "1" },
          "sourceDescriptions": [{ "name": "counter", "url": "./counter.json", "type": "openapi" }],
          "workflows": [{ "workflowId": "flow", "steps": {{steps}} }]
        }
        """;

    [Test]
    public async Task Retry_repeats_a_step_until_its_criteria_hold()
    {
        var steps = """
            [
              { "stepId": "bump", "operationId": "counter.bump" },
              { "stepId": "wait", "operationId": "read",
                "successCriteria": [{ "condition": "$response.body#/n >= 3" }],
                "onFailure": [{ "name": "again", "type": "retry", "retryAfter": 0, "retryLimit": 5, "criteria": [{ "condition": "$statusCode == 200" }] }] }
            ]
            """;

        var report = await Run(steps);

        report.Workflows.Single().Exchanges.Select(e => $"{e.Request.Method} {e.Body}").Should().Equal(
            "POST {\"n\":1}", "GET {\"n\":2}", "GET {\"n\":3}");
        report.Failures.Should().BeEmpty();
    }

    [Test]
    public async Task Goto_and_end_change_the_order_and_a_criterion_that_fails_is_reported()
    {
        var steps = """
            [
              { "stepId": "first", "operationId": "bump", "onSuccess": [{ "name": "skip", "type": "goto", "stepId": "last" }] },
              { "stepId": "skipped", "operationId": "bump" },
              { "stepId": "last", "operationId": "read", "successCriteria": [{ "condition": "$response.body#/n == 99" }] }
            ]
            """;

        var report = await Run(steps);

        report.Workflows.Single().Exchanges.Select(e => e.Request.Method).Should().Equal("POST", "GET");
        report.Failures.Should().ContainSingle().Which.Should().Match<Finding>(f =>
            f.Check == Checks.WorkflowCriteria && f.Subject == "Counter: flow, step last" && f.Message.StartsWith("not met: $response.body#/n == 99", StringComparison.Ordinal));
    }

    [Test]
    public void What_the_engine_can_t_run_is_a_gap_not_a_wrong_run()
    {
        var suite = RegressionSuite.FromJson(Spec, Workflow("""
            [
              { "stepId": "a", "operationPath": "{$sourceDescriptions.counter.url}#/paths/~1counter/delete" },
              { "stepId": "b", "operationId": "nope" },
              { "stepId": "c", "workflowId": "$sourceDescriptions.other.workflows.x" },
              { "stepId": "d", "operationId": "read", "successCriteria": [{ "context": "$response.body", "condition": "//n", "type": "xpath" }] }
            ]
            """));

        suite.Gaps.Should().Equal(
            "Counter: flow: step a names {$sourceDescriptions.counter.url}#/paths/~1counter/delete, which isn't an operation in the spec",
            "Counter: flow: step b calls nope, which the spec doesn't have",
            "Counter: flow: step c runs the workflow $sourceDescriptions.other.workflows.x, which isn't in any Arazzo document given",
            "Counter: flow: step d has a xpath criterion, which isn't supported");
    }

    [Test]
    public async Task An_operationPath_names_an_operation_by_its_path_and_method()
    {
        var report = await Run("""
            [{ "stepId": "read", "operationPath": "{$sourceDescriptions.counter.url}#/paths/~1counter/get", "successCriteria": [{ "condition": "$response.body#/n == 1" }] }]
            """);

        report.Workflows.Single().Exchanges.Select(e => e.Request.Method).Should().Equal("GET");
        report.Failures.Should().BeEmpty();
    }

    private static async Task<RegressionReport> Run(string steps)
    {
        using var client = new HttpClient(new Counter()) { BaseAddress = new Uri("http://counter/") };
        return await RegressionSuite.FromJson(Spec, Workflow(steps)).RunAsync(client, new RegressionOptions { Include = _ => false });
    }

    /// <summary>A counter that every request moves on by one.</summary>
    private sealed class Counter : HttpMessageHandler
    {
        private int _n;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""{"n":{{++_n}}}""", Encoding.UTF8, "application/json"),
            });
    }
}
