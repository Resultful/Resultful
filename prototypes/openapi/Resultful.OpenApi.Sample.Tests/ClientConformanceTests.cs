using System.Net;
using System.Text;
using AwesomeAssertions;
using NUnit.Framework;
using PetStore.Api;

namespace Resultful.OpenApi.Sample.Tests;

/// <summary>
/// The generated client checks every response against the spec, so any test that calls a server through it is also a
/// conformance test of that server. These point the client at canned responses, the way it would see a server that
/// gets the contract wrong.
/// </summary>
[TestFixture]
public sealed class ClientConformanceTests
{
    [Test]
    public async Task An_undocumented_status_is_a_violation()
    {
        var client = Client(HttpStatusCode.InternalServerError, "application/problem+json", """{"title":"boom"}""");

        var act = () => client.GetPet(1, default);

        (await act.Should().ThrowAsync<ContractViolationException>())
            .WithMessage("getPet returned 500: the spec does not document this status");
    }

    [Test]
    public async Task A_problem_sent_as_plain_json_is_a_violation()
    {
        // The bug the prototype's own server had: TypedResults.NotFound(problem) writes application/json.
        var client = Client(HttpStatusCode.NotFound, "application/json", """{"title":"Not found","status":404}""");

        var act = () => client.GetPet(1, default);

        (await act.Should().ThrowAsync<ContractViolationException>())
            .WithMessage("getPet returned 404: the content type is 'application/json', the spec says 'application/problem+json'");
    }

    [Test]
    public async Task A_body_that_breaks_the_schema_is_a_violation()
    {
        var name = new string('x', 51);
        var client = Client(HttpStatusCode.OK, "application/json",
            $$$"""{"id":1,"name":"{{{name}}}","status":"available","details":{"kind":"cat","livesLeft":12}}""");

        var act = () => client.GetPet(1, default);

        (await act.Should().ThrowAsync<ContractViolationException>())
            .WithMessage("getPet returned 200: the body breaks the schema: *name: Must be at most 50 characters.*details.livesLeft: Must be at most 9.*");
    }

    [Test]
    public async Task A_body_missing_a_required_property_is_a_violation()
    {
        var client = Client(HttpStatusCode.OK, "application/json", """{"id":1,"name":"Rex","status":"available"}""");

        var act = () => client.GetPet(1, default);

        (await act.Should().ThrowAsync<ContractViolationException>())
            .WithMessage("getPet returned 200: the body does not match the schema: *details*");
    }

    [Test]
    public async Task A_conforming_response_is_the_documented_case()
    {
        var client = Client(HttpStatusCode.NotFound, "application/problem+json", """{"title":"Not found","status":404}""");

        var result = await client.GetPet(1, default);

        result.ToOneOf().Value.Should().BeOfType<GetPetError>().Which.Value.Should().BeOfType<GetPetNotFound>();
        client.Observed.Should().Equal([("getPet", 404)]);
    }

    private static PetStoreClient Client(HttpStatusCode status, string mediaType, string body)
        => new(new HttpClient(new Canned(status, mediaType, body)) { BaseAddress = new Uri("http://pets.test/") });

    private sealed class Canned(HttpStatusCode status, string mediaType, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) });
    }
}
