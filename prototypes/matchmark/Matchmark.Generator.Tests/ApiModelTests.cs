using AwesomeAssertions;
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
}
