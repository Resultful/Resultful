using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace Matchmark.Generator.Tests;

/// <summary>
/// The model's own tests (docs/reference/model.md): the decisions every output is a function of, pinned from small specs,
/// independent of the code any output writes.
/// </summary>
[TestFixture]
public sealed class ApiModelTests
{
    private const string Spec = """
        openapi: 3.1.1
        info: { title: Shelter, version: 1.0.0 }
        paths:
          /pets:
            get:
              operationId: listPets
              tags: [pets]
              parameters:
                - { name: limit, in: query, schema: { type: integer, format: int32 } }
                - { name: X-Trace, in: header, schema: { type: string } }
              responses:
                '200': { description: The pets, content: { application/json: { schema: { type: array, items: { $ref: '#/components/schemas/Pet' } } } } }
            post:
              operationId: createPet
              tags: [pets]
              requestBody: { required: true, content: { application/json: { schema: { $ref: '#/components/schemas/NewPet' } } } }
              responses:
                '201': { description: Created, content: { application/json: { schema: { $ref: '#/components/schemas/Pet' } } } }
                '400': { description: Invalid, content: { application/problem+json: { schema: { $ref: '#/components/schemas/HttpValidationProblemDetails' } } } }
                '409': { description: Taken }
          /pets/{petId}:
            get:
              operationId: getPet
              tags: [pets]
              parameters: [ { name: petId, in: path, required: true, schema: { type: integer, format: int64 } } ]
              responses:
                '200': { description: The pet, content: { application/json: { schema: { $ref: '#/components/schemas/Pet' } } } }
                '404': { description: None }
        components:
          schemas:
            NewPet: { type: object, required: [name], properties: { name: { type: string } } }
            Pet: { type: object, required: [id, name], properties: { id: { type: integer, format: int64 }, name: { type: string } } }
            HttpValidationProblemDetails: { type: object, properties: { errors: { type: object, additionalProperties: { type: array, items: { type: string } } } } }
        """;

    private static ModelOperation Operation(string id) => ApiModel.Read(Spec, "shelter.yaml", "Shelter")!.Operations.Single(o => o.Id == id);

    [Test]
    public void Every_operation_is_read_with_its_method_path_and_tag()
        => ApiModel.Read(Spec, "shelter.yaml", "Shelter")!.Operations.Select(o => $"{o.Method} {o.Path} {o.Tag}")
            .Should().BeEquivalentTo("GET /pets pets", "POST /pets pets", "GET /pets/{petId} pets");

    [Test]
    public void Parameters_are_typed_and_bound_from_where_the_spec_says()
    {
        Operation("listPets").Parameters.Select(p => (p.Name, p.Binding, p.Type))
            .Should().Equal(("limit", "FromQuery", "int?"), ("X-Trace", "FromHeader", "string?"));
        Operation("getPet").Parameters.Should().ContainSingle(p => p.Name == "petId" && p.Type == "long" && p.Binding == "FromRoute");
    }

    [Test]
    public void A_body_is_a_validated_model_and_outcomes_are_split_into_successes_and_errors()
    {
        var create = Operation("createPet");
        create.Body.Should().Be(new ModelBody("NewPet", "application/json", Required: true, Validated: true));
        create.Outcomes.Should().Equal(
            new ModelOutcome(201, "Pet", Success: true, Validation: false),
            new ModelOutcome(400, "global::Microsoft.AspNetCore.Http.HttpValidationProblemDetails", Success: false, Validation: true),
            new ModelOutcome(409, null, Success: false, Validation: false));
        Operation("listPets").Outcomes.Should().ContainSingle(o => o.Status == 200 && o.Type == "global::System.Collections.Generic.IReadOnlyList<Pet>");
    }

    [Test]
    public void What_the_spec_leaves_out_is_a_diagnostic_of_the_model()
    {
        var model = ApiModel.Read(Spec.Replace("      operationId: listPets\n", string.Empty, StringComparison.Ordinal), "shelter.yaml")!;
        model.Diagnostics.Should().Contain("MM003: GET /pets has no operationId; it was skipped");
        model.Operations.Should().HaveCount(2);
    }

    /// <summary>
    /// Plain outcomes (Q12, the default): the generated contract and server compile, with a handler, without Resultful or
    /// OneOf among the references at all.
    /// </summary>
    [Test]
    public void A_plain_contract_needs_neither_Resultful_nor_OneOf()
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Where(p => !Path.GetFileName(p).StartsWith("Resultful", StringComparison.Ordinal) && !Path.GetFileName(p).StartsWith("OneOf", StringComparison.Ordinal))
            .Select(p => (Microsoft.CodeAnalysis.MetadataReference)Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(p));
        const string Handler = """
            using System.Threading;
            using System.Threading.Tasks;

            namespace Shelter.Api;

            public sealed class Pets : IPetsHandlers
            {
                // No errors documented, so no Result: the list itself.
                public Task<global::System.Collections.Generic.IReadOnlyList<Pet>> ListPets(int? limit, string? xTrace, CancellationToken cancellationToken)
                    => Task.FromResult<global::System.Collections.Generic.IReadOnlyList<Pet>>(new[] { new Pet { Id = 1, Name = "Rex" } });

                public Task<Result<Pet, CreatePetError>> CreatePet(NewPet body, CancellationToken cancellationToken)
                    => Task.FromResult(Result.Fail<Pet, CreatePetError>(CreatePetError.Conflict()));

                public Task<Result<Pet, GetPetError>> GetPet(long petId, CancellationToken cancellationToken)
                    => Task.FromResult(Result.Fail<Pet, GetPetError>(GetPetError.NotFound()));
            }
            """;
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("Plain", [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(Handler)], references,
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: Microsoft.CodeAnalysis.NullableContextOptions.Enable));
        var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "shelter-plain.yaml");
        File.WriteAllText(path, Spec);
        var driver = Microsoft.CodeAnalysis.CSharp.CSharpGeneratorDriver.Create([new OpenApiSourceGenerator().AsSourceGenerator()], [new SpecText(path, Spec)], optionsProvider: new PlainOptions());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var generated, out _);

        generated.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).Select(d => d.ToString()).Take(10).Should().BeEmpty();
    }

    private sealed class SpecText(string path, string text) : Microsoft.CodeAnalysis.AdditionalText
    {
        public override string Path => path;

        public override Microsoft.CodeAnalysis.Text.SourceText GetText(CancellationToken cancellationToken = default) => Microsoft.CodeAnalysis.Text.SourceText.From(text);
    }

    private sealed class PlainOptions : Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptionsProvider
    {
        private static readonly Values Spec = new(new Dictionary<string, string>
        {
            ["build_metadata.AdditionalFiles.Matchmark"] = "true",
            ["build_metadata.AdditionalFiles.MatchmarkApiName"] = "Shelter",
            ["build_metadata.AdditionalFiles.MatchmarkNamespace"] = "Shelter.Api",
            ["build_metadata.AdditionalFiles.MatchmarkParts"] = "Contract,Server",
        });

        public override Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions GlobalOptions { get; } = new Values([]);

        public override Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions GetOptions(Microsoft.CodeAnalysis.SyntaxTree tree) => GlobalOptions;

        public override Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions GetOptions(Microsoft.CodeAnalysis.AdditionalText textFile) => Spec;

        private sealed class Values(Dictionary<string, string> values) : Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
        }
    }
}
