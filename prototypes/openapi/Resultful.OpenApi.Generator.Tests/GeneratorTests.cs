using System.Collections.Immutable;
using FluentCheck;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;

namespace Resultful.OpenApi.Generator.Tests;

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

        diagnostics.Select(d => (d.Id, d.Severity)).Should().Contain(("RAPI001", DiagnosticSeverity.Error));
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

        diagnostics.Select(d => d.Id).Should().BeInExactOrder(["RAPI003"]);
        sources["T.Endpoints.g.cs"].Should().NotContain("MapMethods");
    }

    [Test]
    public void An_inline_object_schema_is_reported_as_unsupported()
    {
        var (diagnostics, _) = Run(Header + """

            paths:
              /things:
                get:
                  operationId: getThing
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
                            properties: { a: { type: string } }
            """);

        diagnostics.Should().ContainSingle(d => d.Id == "RAPI002" && d.GetMessage().Contains("inline object"));
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

        diagnostics.Select(d => d.Id).Should().BeInExactOrder(["RAPI004"]);
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
        sources["T.Responses.g.cs"].Should().Contain("public sealed class GetThingError : OneOfBase<GetThingNotFound, GetThingConflict>");
        sources["T.Endpoints.g.cs"].Should().Contain("Results<Ok<Thing>, NotFound, Conflict>");
    }

    private static (ImmutableArray<Diagnostic> Diagnostics, Dictionary<string, string> Sources) Run(string yaml)
    {
        var driver = CSharpGeneratorDriver.Create(
            [new OpenApiSourceGenerator().AsSourceGenerator()],
            [new Spec("/specs/t.yaml", yaml)],
            optionsProvider: new Options());

        var result = driver.RunGenerators(CSharpCompilation.Create("Test")).GetRunResult();
        return (result.Diagnostics, result.Results.SelectMany(r => r.GeneratedSources)
            .ToDictionary(s => s.HintName, s => s.SourceText.ToString()));
    }

    private sealed class Spec(string path, string text) : AdditionalText
    {
        public override string Path => path;

        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text);
    }

    private sealed class Options : AnalyzerConfigOptionsProvider
    {
        private static readonly AnalyzerConfigOptions Marked = new Values(new()
        {
            ["build_metadata.AdditionalFiles.ResultfulOpenApi"] = "true",
            ["build_metadata.AdditionalFiles.ResultfulApiName"] = "T",
        });

        public override AnalyzerConfigOptions GlobalOptions { get; } = new Values([]);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => Marked;

        private sealed class Values(Dictionary<string, string> values) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
        }
    }
}
