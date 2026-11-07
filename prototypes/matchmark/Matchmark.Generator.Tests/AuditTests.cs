using System.Text.Json.Nodes;
using AwesomeAssertions;
using Matchmark.Cli;
using NUnit.Framework;

namespace Matchmark.Generator.Tests;

/// <summary><c>matchmark audit</c>: what a spec leaves out, each finding measured by generating the suite with its change made.</summary>
[TestFixture]
public sealed class AuditTests
{
    private const string Spec = """
        openapi: 3.1.0
        info: { title: Notes, version: '1' }
        security: [{ key: [] }]
        components:
          securitySchemes: { key: { type: apiKey, in: header, name: X-Key } }
          schemas:
            NewNote:
              type: object
              required: [text]
              properties:
                id: { type: string, format: uuid }
                text: { type: string }
                stars: { type: integer }
            Note:
              type: object
              properties:
                id: { type: string, format: uuid }
                text: { type: string }
        paths:
          /notes:
            post:
              operationId: addNote
              requestBody: { required: true, content: { application/json: { schema: { $ref: '#/components/schemas/NewNote' } } } }
              responses:
                '201': { description: added, content: { application/json: { schema: { $ref: '#/components/schemas/Note' } } } }
                '400': { description: invalid }
                '401': { description: who are you }
          /notes/{noteId}:
            get:
              operationId: getNote
              parameters: [{ name: noteId, in: path, required: true, schema: { type: string, format: uuid } }]
              responses:
                '200': { description: the note, content: { application/json: { schema: { $ref: '#/components/schemas/Note' } } } }
            put:
              parameters: [{ name: noteId, in: path, required: true, schema: { type: string, format: uuid } }]
              requestBody: { required: true, content: { application/json: { schema: { $ref: '#/components/schemas/NewNote' } } } }
              responses:
                '200': { description: replaced }
        """;

    private static AuditCommand.Result Audit() => AuditCommand.Measure(SpecJson.Normalize(Spec, "notes.yaml"));

    private static string Line(AuditCommand.Measured m) => $"{m.Finding.Rule.Id} {m.Finding.Subject}: {m.Finding.Message}";

    [Test]
    public void It_finds_what_the_spec_leaves_out()
        => Audit().Findings.Select(Line).Should().BeEquivalentTo(
            "operation-id PUT /notes/{noteId}: has no operationId",
            "rejection-documented getNote: acts on one resource but documents no 404",
            "security-documented getNote: is secured but documents no 401",
            "rejection-documented PUT /notes/{noteId}: documents no 400: how bad input is rejected is unchecked",
            "rejection-documented PUT /notes/{noteId}: acts on one resource but documents no 404",
            "security-documented PUT /notes/{noteId}: is secured but documents no 401",
            "concurrency PUT /notes/{noteId}: changes a resource without If-Match: a concurrent change can be lost",
            "request-example addNote: the application/json body has no example",
            "request-example PUT /notes/{noteId}: the application/json body has no example",
            "idempotency-key addNote: creates without an Idempotency-Key header: a retried request can create twice",
            "create-linked addNote: has no links, and no Arazzo workflow reaches it: nothing checks what it created can be read back",
            "additional-properties NewNote: doesn't say whether other properties are allowed",
            "read-only NewNote.id: looks assigned by the server but isn't readOnly",
            "bounded-string NewNote.text: is a string with no maxLength, pattern, format or enum",
            "bounded-number NewNote.stars: is a number with no minimum or maximum",
            "invariants the spec: states no invariants (x-matchmark-ensures, x-matchmark-rules, x-matchmark-transitions, x-dependencies); see COMPLETENESS.md");

    [Test]
    public void Each_change_is_measured_in_what_it_adds_to_the_suite()
    {
        var measured = Audit().Findings.Where(m => m.Finding.Rule.Id != "rejection-documented").ToDictionary(m => $"{m.Finding.Rule.Id} {m.Finding.Subject}", m => (m.Cases, m.Workflows));

        measured["create-linked addNote"].Should().Be((0, 1), "a link to getNote is one more workflow");
        measured["bounded-string NewNote.text"].Cases.Should().BePositive("maxLength adds a boundary and a too-long case");
        measured["bounded-number NewNote.stars"].Cases.Should().BePositive();
        measured["read-only NewNote.id"].Cases.Should().BePositive("a read-only property sent is a case of its own");
        measured["additional-properties NewNote"].Cases.Should().BePositive("an unknown property sent is a case of its own");
        measured["request-example addNote"].Cases.Should().Be(1);
    }

    [Test]
    public void A_rejection_left_undocumented_says_how_many_cases_it_leaves_unchecked()
        => Audit().Findings.Single(m => m.Finding.Message.StartsWith("documents no 400", StringComparison.Ordinal)).Unlocks
            .Should().MatchRegex(@"^\d+ invalid cases end in a warning, not a check\. Document the 400\.$");

    [Test]
    public void Arazzo_reaching_a_create_counts_as_linking_it()
    {
        const string arazzo = """{"arazzo":"1.0.1","info":{"title":"t","version":"1"},"sourceDescriptions":[],"workflows":[{"workflowId":"w","steps":[{"stepId":"s","operationId":"addNote"}]}]}""";

        AuditCommand.Measure(SpecJson.Normalize(Spec, "notes.yaml"), arazzo).Findings.Should().NotContain(m => m.Finding.Rule.Id == "create-linked");
    }

    [Test]
    public void The_SARIF_names_every_rule_and_places_each_result_in_the_spec()
    {
        var result = Audit();
        var sarif = JsonNode.Parse(AuditCommand.Sarif(result, "api/notes.yaml"))!;
        var run = sarif["runs"]![0]!;

        sarif["version"]!.GetValue<string>().Should().Be("2.1.0");
        run["tool"]!["driver"]!["rules"]!.AsArray().Select(r => r!["id"]!.GetValue<string>()).Should().Equal(Matchmark.Generator.Audit.Rules.Select(r => r.Id));
        run["results"]!.AsArray().Should().HaveCount(result.Findings.Count)
            .And.AllSatisfy(r => r!["locations"]![0]!["physicalLocation"]!["artifactLocation"]!["uri"]!.GetValue<string>().Should().Be("api/notes.yaml"));
        run["results"]!.AsArray().Select(r => r!["locations"]![0]!["logicalLocations"]![0]!["fullyQualifiedName"]!.GetValue<string>())
            .Should().Contain("#/components/schemas/NewNote/properties/text");
    }

    [Test]
    public void The_Spectral_ruleset_is_YAML_with_a_rule_for_each_static_check()
    {
        var stream = new SharpYaml.Serialization.YamlStream();
        stream.Load(new StringReader(AuditCommand.Spectral()));
        var rules = Microsoft.OpenApi.YamlReader.YamlConverter.ToJsonNode(stream.Documents[0])["rules"]!.AsObject();

        rules.Select(r => r.Key).Should().Contain(["matchmark-operation-id", "matchmark-bounded-string", "matchmark-create-linked"]);
        rules.Should().AllSatisfy(r => r.Value!["given"].Should().NotBeNull());
    }

    [Test]
    public async Task The_command_prints_the_report_and_writes_SARIF()
    {
        var directory = Directory.CreateTempSubdirectory("matchmark-audit").FullName;
        var spec = Path.Combine(directory, "notes.yaml");
        await File.WriteAllTextAsync(spec, Spec);
        using var output = new StringWriter();

        var exit = await AuditCommand.RunAsync([spec, "--sarif", Path.Combine(directory, "audit.sarif"), "--strict"], output);

        exit.Should().Be(1, "--strict fails on a warning");
        output.ToString().Should().Contain("create-linked (warning)").And.MatchRegex(@"Score: \d+% of what the suite could check is checkable");
        File.Exists(Path.Combine(directory, "audit.sarif")).Should().BeTrue();
    }
}
