using System.Collections.Immutable;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;

namespace Matchmark.Generator.Tests;

/// <summary>What the generator does with specs outside the prototype's scope: it reports, it does not emit broken code.</summary>
[TestFixture]
public sealed class GeneratorTests
{
    private const string Header = """
        openapi: 3.1.0
        info: { title: T, version: '1' }
        """;

    [Test]
    public void An_unreadable_spec_is_an_error()
    {
        var (diagnostics, _) = Run("openapi: [unclosed");

        diagnostics.Select(d => (d.Id, d.Severity)).Should().Contain(("MM001", DiagnosticSeverity.Error));
    }

    [Test]
    public void An_operation_without_an_operationId_is_skipped_with_a_warning()
    {
        var (diagnostics, sources) = Run(Header + """

            paths:
              /things:
                get:
                  responses:
                    '204': { description: none }
            """);

        diagnostics.Select(d => d.Id).Should().Equal("MM003");
        sources["T.Endpoints.g.cs"].Should().NotContain("MapMethods");
    }

    [Test]
    public void Inline_objects_allOf_aliases_and_any_status_are_generated()
    {
        var (diagnostics, sources) = Run(Header + """

            paths:
              /things/{id}:
                parameters: [{ name: id, in: path, required: true, schema: { $ref: '#/components/schemas/ThingId' } }]
                get:
                  operationId: getThing
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
                            required: [id, tags]
                            properties:
                              id: { $ref: '#/components/schemas/ThingId' }
                              tags: { $ref: '#/components/schemas/Tags' }
                              owner: { type: object, properties: { name: { type: string } } }
                    '412': { description: stale, content: { application/json: { schema: { $ref: '#/components/schemas/Stamped' } } } }
                    '429': { description: slow down }
            components:
              schemas:
                ThingId: { type: string, format: uuid }
                Tags: { type: array, items: { type: string, maxLength: 10 } }
                Base: { type: object, required: [at], properties: { at: { type: string, format: date-time } } }
                Stamped:
                  allOf:
                    - $ref: '#/components/schemas/Base'
                    - type: object
                      properties: { by: { type: string } }
            """, parts: "Contract,Server");

        diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning).Should().BeEmpty();
        var models = sources["T.Models.g.cs"];
        models.Should().Contain("public sealed record GetThingOkBody").And.Contain("public required global::System.Guid Id")
            .And.Contain("IReadOnlyList<string> Tags").And.Contain("public sealed record GetThingOkBodyOwner")
            .And.Contain("public sealed record Stamped").And.Contain("global::System.DateTimeOffset At")
            .And.NotContain("record ThingId").And.NotContain("record Tags");
        sources["T.Endpoints.g.cs"].Should().Contain("TypedResults.Json(").And.Contain("TypedResults.StatusCode(429)")
            .And.Contain(".Produces<Stamped>(412, \"application/json\")").And.Contain(".Produces(429)");
    }


    [Test]
    public void A_request_body_without_a_documented_validation_response_is_a_warning()
    {
        var (diagnostics, sources) = Run(Header + """

            paths:
              /things:
                post:
                  operationId: createThing
                  requestBody:
                    content:
                      application/json:
                        schema: { $ref: '#/components/schemas/Thing' }
                  responses:
                    '204': { description: none }
            components:
              schemas:
                Thing:
                  type: object
                  properties: { name: { type: string, minLength: 2 } }
            """);

        diagnostics.Select(d => d.Id).Should().Equal("MM004");
        sources["T.Endpoints.g.cs"].Should().Contain("Results<NoContent, ValidationProblem>");
    }

    [Test]
    public void One_success_and_errors_becomes_a_Result_of_the_body_and_an_error_union()
    {
        var (diagnostics, sources) = Run(Header + """

            paths:
              /things/{id}:
                get:
                  operationId: getThing
                  parameters:
                    - { name: id, in: path, required: true, schema: { type: string, format: uuid } }
                  responses:
                    '200':
                      description: ok
                      content: { application/json: { schema: { $ref: '#/components/schemas/Thing' } } }
                    '404': { description: missing }
                    '409': { description: conflict }
            components:
              schemas:
                Thing:
                  type: object
                  properties: { name: { type: string } }
            """);

        diagnostics.Should().BeEmpty();
        sources["T.Handlers.g.cs"].Should().Contain("Task<Result<Thing, GetThingError>> GetThing(global::System.Guid id, CancellationToken cancellationToken);");
        // Plain outcomes (Q12, the default): the union is the contract's own, with OneOf's Match, and Result is too.
        sources["T.Responses.g.cs"].Should().Contain("public sealed class GetThingError").And.NotContain("OneOf")
            .And.Contain("public TResult Match<TResult>(global::System.Func<GetThingNotFound, TResult> f0, global::System.Func<GetThingConflict, TResult> f1)");
        sources["T.Outcomes.g.cs"].Should().Contain("public readonly struct Result<T, TError>").And.Contain("public static class Result");
        string.Concat(sources.Values).Should().NotContain("Resultful", "a plain contract depends on neither Resultful nor OneOf");
        sources["T.Endpoints.g.cs"].Should().Contain("Results<Ok<Thing>, NotFound, Conflict>");
    }

    [Test]
    public void A_required_string_whose_pattern_the_sample_cannot_meet_asks_for_an_example()
    {
        const string spec = Header + """

            paths:
              /codes:
                get:
                  operationId: getCode
                  responses:
                    '200':
                      description: ok
                      content: { application/json: { schema: { $ref: '#/components/schemas/Code' } } }
            components:
              schemas:
                Code:
                  type: object
                  required: [value]
                  properties: { value: { type: string, pattern: '^[0-9]{4}$' } }
            """;

        Run(spec).Diagnostics.Should().ContainSingle(d => d.Id == "MM005" && d.GetMessage().Contains("Code.value"));
        Run(spec.Replace("pattern: '^[0-9]{4}$'", "pattern: '^[0-9]{4}$', example: '1234'")).Diagnostics.Should().BeEmpty();
    }

    [Test]
    public void The_OpenAPI_document_configuration_is_only_emitted_for_apps_that_use_Microsoft_AspNetCore_OpenApi()
    {
        const string spec = Header + """

            paths: {}
            components:
              schemas:
                Status: { type: string, enum: [on, off] }
                Details:
                  oneOf: [{ $ref: '#/components/schemas/A' }, { $ref: '#/components/schemas/B' }]
                  discriminator: { propertyName: kind }
                A: { type: object, properties: { kind: { type: string }, a: { type: integer } } }
                B: { type: object, properties: { kind: { type: string }, b: { type: integer } } }
            """;

        Run(spec).Sources.Keys.Should().NotContain("T.OpenApi.g.cs");

        var withOpenApi = Run(spec, "namespace Microsoft.AspNetCore.OpenApi { public sealed class OpenApiOptions { } }").Sources;
        withOpenApi["T.OpenApi.g.cs"].Should()
            .Contain("(\"Details\", \"kind\", [(\"A\", \"A\"), (\"B\", \"B\")])")
            .And.Contain("StringEnums = [\"Status\"]")
            .And.Contain("public static OpenApiOptions AddTSchemas(this OpenApiOptions options)");
    }

    [TestCase(null, new[] { "Client", "Endpoints", "Fake", "Handlers", "Models", "Outcomes", "Responses", "Validation" })]
    [TestCase("Contract", new[] { "Client", "Fake", "Handlers", "Models", "Outcomes", "Responses", "Validation" })]
    [TestCase("Server", new[] { "Endpoints" })]
    [TestCase("Server,FakeServer", new[] { "Endpoints", "FakeHosting" })]
    [TestCase("Server;FakeServer", new[] { "Endpoints", "FakeHosting" })]
    public void Each_project_generates_only_its_parts(string? parts, string[] files)
    {
        const string spec = Header + """

            paths:
              /things:
                get:
                  operationId: listThings
                  responses:
                    '204': { description: none }
            """;

        var sources = Run(spec, "namespace Matchmark.Fakes { public sealed class Scenario { } }", parts).Sources;

        sources.Keys.Select(k => k.Split('.')[1]).Order().Should().Equal(files);
    }

    [Test]
    public void A_part_the_reader_objects_to_is_a_warning_and_the_rest_is_generated()
    {
        // OpenAPI 3.2 deprecates xml.wrapped but still allows it; Microsoft.OpenApi 3.11 refuses it, as it does in
        // Bump.sh's Train Travel API. The objection is a warning, and the rest is generated.
        const string spec = """
            openapi: 3.2.0
            info: { title: T, version: '1' }
            paths:
              /things:
                get:
                  operationId: listThings
                  responses:
                    '200': { description: ok, content: { application/json: { schema: { $ref: '#/components/schemas/Things' } } } }
            components:
              schemas:
                Things:
                  type: object
                  properties:
                    items:
                      type: array
                      xml: { name: things, wrapped: true }
                      items: { type: string }
            """;

        var (diagnostics, sources) = Run(spec, parts: "Contract");

        diagnostics.Select(d => d.Id).Should().Equal("MM007");
        diagnostics[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture).Should().Contain("wrapped is not a valid property");
        sources.Keys.Should().Contain("T.Models.g.cs");
        SpecJson.Normalize(spec, "t.yaml", out var problems).Should().Contain("\"listThings\"");
        problems.Should().ContainSingle();
    }

    [Test]
    public void The_Regression_part_writes_the_spec_as_JSON_for_the_regression_engine()
    {
        const string spec = Header + """

            paths:
              /things:
                get:
                  operationId: listThings
                  responses:
                    '204': { description: none }
            """;

        var without = Run(spec, parts: "Regression");
        without.Sources.Should().BeEmpty();
        without.Diagnostics.Select(d => d.Id).Should().Equal("MM006");

        var with = Run(spec, "namespace Matchmark.Testing { public sealed class RegressionSuite { } }", "Regression");
        with.Diagnostics.Should().BeEmpty();
        with.Sources.Keys.Should().Equal("T.Regression.g.cs");
        with.Sources["T.Regression.g.cs"].Should()
            .Contain("public static class TRegression")
            .And.Contain("public const string Spec = @\"{")
            .And.Contain("\"\"openapi\"\": \"\"3.1.")
            .And.Contain("\"\"operationId\"\": \"\"listThings\"\"")
            .And.Contain("public static readonly string[] Workflows = new[] {  };")
            .And.Contain("RegressionSuite.FromJson(Spec, Workflows)");
    }

    [Test]
    public void MatchmarkAudit_reports_what_the_spec_leaves_out_as_MM024()
    {
        const string spec = Header + """

            paths:
              /things:
                get:
                  operationId: listThings
                  parameters: [{ name: q, in: query, schema: { type: string } }]
                  responses:
                    '204': { description: none }
            """;

        Run(spec, parts: "Contract").Diagnostics.Should().BeEmpty("the audit is opt-in");

        var audited = Run(spec, parts: "Contract", audit: true).Diagnostics;
        audited.Should().AllSatisfy(d => d.Id.Should().Be("MM024"));
        audited.Select(d => d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)).Should().Contain(m =>
            m.StartsWith("listThings query.q is a string with no maxLength, pattern, format or enum (bounded-string)", StringComparison.Ordinal)
            && m.Contains("At #/paths/~1things/get/parameters/0/schema", StringComparison.Ordinal));
        audited.Should().HaveCount(3, "an unbounded string, an undocumented 400 and no invariants");
    }

    private static (ImmutableArray<Diagnostic> Diagnostics, Dictionary<string, string> Sources) Run(string yaml, string? code = null, string? parts = null, bool audit = false)
    {
        var driver = CSharpGeneratorDriver.Create(
            [new OpenApiSourceGenerator().AsSourceGenerator()],
            [new Spec("/specs/t.yaml", yaml)],
            optionsProvider: new Options(parts, audit));

        var compilation = CSharpCompilation.Create("Test", code is null ? [] : [CSharpSyntaxTree.ParseText(code)]);
        var result = driver.RunGenerators(compilation).GetRunResult();
        // This compilation references nothing, so .NET's validation layer is never there to register (MM029): not what these test.
        return (result.Diagnostics.Where(d => d.Id != "MM029").ToImmutableArray(), result.Results.SelectMany(r => r.GeneratedSources)
            .ToDictionary(s => s.HintName, s => s.SourceText.ToString()));
    }

    private sealed class Spec(string path, string text) : AdditionalText
    {
        public override string Path => path;

        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text);
    }

    private sealed class Options(string? parts, bool audit = false) : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions _marked = new Values(new Dictionary<string, string>
        {
            ["build_metadata.AdditionalFiles.MatchmarkAudit"] = audit ? "true" : "false",
            ["build_metadata.AdditionalFiles.Matchmark"] = "true",
            ["build_metadata.AdditionalFiles.MatchmarkApiName"] = "T",
            ["build_metadata.AdditionalFiles.MatchmarkParts"] = parts ?? string.Empty,
        });

        public override AnalyzerConfigOptions GlobalOptions { get; } = new Values([]);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _marked;

        private sealed class Values(Dictionary<string, string> values) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
        }
    }
}
