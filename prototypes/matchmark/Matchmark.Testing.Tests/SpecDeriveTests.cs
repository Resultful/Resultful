using AwesomeAssertions;
using NUnit.Framework;

namespace Matchmark.Testing.Tests;

/// <summary>matchmark derive's core (docs/reference/flip.md, step 1): operationIds where ASP.NET Core left them out, and what to review.</summary>
[TestFixture]
public sealed class SpecDeriveTests
{
    [TestCase("get", "/pets", "getPets")]
    [TestCase("get", "/pets/{petId}/photos", "getPetsPhotosByPetId")]
    [TestCase("delete", "/owners/{owner-id}/pets/{petId}", "deleteOwnersPetsByOwnerIdAndPetId")]
    public void Operation_ids_come_from_the_method_and_path(string method, string path, string id)
        => SpecDerive.OperationId(method, path).Should().Be(id);

    [Test]
    public void A_document_without_operation_ids_gets_unique_ones_and_a_review()
    {
        const string Document = """
            { "openapi": "3.1.1", "info": { "title": "Api", "version": "1.0.0" }, "servers": [ { "url": "http://localhost:5000/" } ],
              "paths": {
                "/pets": { "get": { "responses": { "200": { "description": "OK" } } }, "post": { "requestBody": { "content": { "application/json": { "schema": { "type": "object" } } } }, "responses": { "201": { "description": "Created" } } } },
                "/pets/{petId}": { "get": { "operationId": "getPets", "responses": { "200": { "description": "OK" } } } } } }
            """;
        var (spec, review) = SpecDerive.Derive(Document);

        spec["paths"]!["/pets"]!["get"]!["operationId"]!.GetValue<string>().Should().Be("getPets2", "getPets is taken by a named endpoint");
        spec["paths"]!["/pets"]!["post"]!["operationId"]!.GetValue<string>().Should().Be("postPets");
        review.Should().Contain(r => r.StartsWith("POST /pets: takes a body but documents no 400", StringComparison.Ordinal))
            .And.Contain(r => r.StartsWith("GET /pets/{petId}: documents only 200", StringComparison.Ordinal))
            .And.Contain(r => r.StartsWith("servers: http://localhost:5000/", StringComparison.Ordinal))
            .And.Contain(r => r.StartsWith("info.version is 1.0.0", StringComparison.Ordinal));
    }
}
