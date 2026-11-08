using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Matchmark.Cli;
using Matchmark.Testing;
using NUnit.Framework;

namespace Matchmark.Generator.Tests;

/// <summary>
/// Forward porting (decisions Q22 to Q25): AsyncAPI 2.x by the official migration guide, Arazzo 1.0's JSONPath kept by
/// naming it, OpenAPI written down to 3.1, and every output read back as the document it was written from.
/// </summary>
[TestFixture]
public sealed class UpgradeTests
{
    private static readonly string Versions = Path.GetFullPath(Path.Combine(ThisDirectory(), "..", "samples", "versions"));

    private const string Events = """
        asyncapi: 2.6.0
        info: { title: Accounts, version: "1.0.0" }
        tags: [ { name: accounts } ]
        externalDocs: { url: "https://example.com/docs" }
        servers:
          production:
            url: amqp://rabbit.example.com:5672/accounts
            protocol: amqp
            security: [ { oauth: [ "write:accounts" ] } ]
        channels:
          user/{userId}/signedup:
            servers: [ production ]
            parameters:
              userId:
                description: The user
                schema: { type: string, enum: [ a, b ], pattern: "^[ab]$" }
            publish:
              operationId: onSignup
              message:
                oneOf:
                  - messageId: Signup
                    payload: { type: object, properties: { name: { type: string } } }
                  - messageId: SignupV2
                    schemaFormat: application/vnd.apache.avro;version=1.9.0
                    payload: { type: record, name: User, fields: [ { name: displayName, type: string } ] }
        components:
          securitySchemes:
            oauth:
              type: oauth2
              flows: { implicit: { authorizationUrl: "https://example.com/auth", availableScopes: { "write:accounts": write } } }
        """;

    [Test]
    public void AsyncApi_2_follows_every_section_of_the_migration_guide()
    {
        var result = SpecUpgrade.Upgrade(Events, "events.yaml");
        var document = result.Document;

        result.To.Should().Be("3.1.0");
        document["info"]!["tags"]!.AsArray().Should().ContainSingle("tags move into info");
        document["info"]!["externalDocs"].Should().NotBeNull();
        document["servers"]!["production"]!["host"]!.GetValue<string>().Should().Be("rabbit.example.com:5672", "the url splits up");
        document["servers"]!["production"]!["pathname"]!.GetValue<string>().Should().Be("/accounts");
        document["servers"]!["production"]!["security"]![0]!["$ref"]!.GetValue<string>().Should().Be("#/components/securitySchemes/oauth");
        document["components"]!["securitySchemes"]!["oauth"]!["scopes"]![0]!.GetValue<string>().Should().Be("write:accounts", "scopes move to the scheme");

        var channel = document["channels"]!["userUserIdSignedupChannel"]!;
        channel["address"]!.GetValue<string>().Should().Be("user/{userId}/signedup", "the key is an id and the path the address");
        channel["servers"]![0]!["$ref"]!.GetValue<string>().Should().Be("#/servers/production", "references are explicit");
        channel["parameters"]!["userId"]!["enum"]!.AsArray().Should().HaveCount(2);
        channel["parameters"]!["userId"]!["schema"].Should().BeNull("3.0 parameters have no schema");
        channel["messages"]!.AsObject().Select(m => m.Key).Should().Equal("Signup", "SignupV2");
        channel["messages"]!["SignupV2"]!["payload"]!["schemaFormat"]!.GetValue<string>().Should().StartWith("application/vnd.apache.avro", "schemaFormat moves onto the payload");
        channel["messages"]!["Signup"]!["messageId"].Should().BeNull("the key is the id now");

        var operation = document["operations"]!["onSignup"]!;
        operation["action"]!.GetValue<string>().Should().Be("receive", "v2's publish means others publish to this application");
        operation["messages"]!.AsArray().Select(m => m!["$ref"]!.GetValue<string>()).Should().Equal(
            "#/channels/userUserIdSignedupChannel/messages/Signup", "#/channels/userUserIdSignedupChannel/messages/SignupV2");

        result.Losses.Should().Contain(l => l.Contains("pattern", StringComparison.Ordinal), "a parameter's pattern has no place in 3.0");
        result.Losses.Should().Contain(l => l.Contains("scopes", StringComparison.Ordinal), "scopes on the scheme apply to every user of it");
    }

    [Test]
    public void A_2_x_document_is_read_by_the_engine_and_the_generator()
    {
        var path = Path.Combine(Versions, "asyncapi-2.6.yaml");
        var document = AsyncApiDocument.Parse(SpecJson.AsyncApi(File.ReadAllText(path), path));

        document.Operations.Select(o => (o.Id, o.Action)).Should().BeEquivalentTo([("receiveAdoption", "receive"), ("publishAdoption", "send")]);
        document.ConsumerSuite()!.Cases.Should().NotBeEmpty();
        GeneratedServer.Build("Shelter", path, [], string.Empty, withRules: false).Diagnostics.Should().NotContain(d => d.Id == "MM001");
    }

    [Test]
    public void Arazzo_1_0_JSONPath_keeps_its_meaning_by_naming_its_dialect()
    {
        var result = SpecUpgrade.Upgrade("""
            arazzo: 1.0.1
            info: { title: t, version: "1" }
            sourceDescriptions: [ { name: s, url: s.yaml } ]
            workflows:
              - workflowId: w
                steps:
                  - stepId: one
                    operationId: getPet
                    successCriteria: [ { context: $response.body, condition: $.name, type: jsonpath } ]
            """, "flow.yaml");

        result.To.Should().Be("1.1.0");
        var type = result.Document["workflows"]![0]!["steps"]![0]!["successCriteria"]![0]!["type"]!;
        type["version"]!.GetValue<string>().Should().Be("draft-goessner-dispatch-jsonpath-00", "1.1 reads JSONPath as RFC 9535 unless told otherwise");
    }

    [Test]
    public async Task OpenAPI_written_down_to_3_1_moves_QUERY_as_ASP_NET_Core_does_and_a_loss_exits_1()
    {
        var path = Path.Combine(Versions, "openapi-3.2.yaml");
        var result = SpecUpgrade.Upgrade(File.ReadAllText(path), path, "3.1");

        result.To.Should().StartWith("3.1");
        result.Losses.Should().BeEmpty();
        result.Changes.Should().Contain(c => c.StartsWith("QUERY /pets/search written as x-oai-additionalOperations", StringComparison.Ordinal));

        using var output = new StringWriter();
        (await UpgradeCommand.RunAsync([Path.Combine(Versions, "openapi-3.2.yaml")], output)).Should().Be(0);
        var events = Path.Combine(TestContext.CurrentContext.WorkDirectory, "lossy-events.yaml");
        await File.WriteAllTextAsync(events, Events);
        (await UpgradeCommand.RunAsync([events, "--output", Path.Combine(TestContext.CurrentContext.WorkDirectory, "upgraded.yaml")], output)).Should().Be(1, "something was lost");
    }

    [TestCaseSource(nameof(Samples))]
    public void Every_sample_upgrades_and_its_YAML_reads_back_as_the_same_document(string file)
    {
        var path = Path.Combine(Versions, file);
        var result = SpecUpgrade.Upgrade(File.ReadAllText(path), path);
        var yaml = SpecUpgrade.Yaml(result.Document);
        var stream = new SharpYaml.Serialization.YamlStream();
        stream.Load(new StringReader(yaml));

        JsonNode.DeepEquals(Microsoft.OpenApi.YamlReader.YamlConverter.ToJsonNode(stream.Documents[0]), result.Document).Should().BeTrue(yaml);
        SpecUpgrade.Upgrade(yaml, "again.yaml").Changes.Should().BeEmpty("an upgraded document is already at the latest version");
    }

    private static IEnumerable<string> Samples() => Directory.GetFiles(Versions, "*.yaml").Select(Path.GetFileName).Order()!;

    private static string ThisDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
