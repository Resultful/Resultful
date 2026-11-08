using System.Runtime.CompilerServices;
using AwesomeAssertions;
using NUnit.Framework;

namespace Matchmark.Generator.Tests;

/// <summary>
/// The unique trait in the generator and the audit (docs/reference/invariants.md#unique): the spec's unique keys as
/// metadata, a declaration that names an unknown field (MM027), a create with no status to refuse with (MM028), and the
/// audit's suggestion where a create documents a 409 but declares no key.
/// </summary>
[TestFixture]
public sealed class UniqueTraitTests
{
    private static readonly string Shelter = Path.GetFullPath(Path.Combine(ThisDirectory(), "..", "samples", "sessions", "openapi.yaml"));

    [Test]
    public void The_contract_carries_the_spec_s_unique_keys_as_data()
    {
        var (assembly, diagnostics) = GeneratedServer.Build("Shelter", Shelter, [], string.Empty, withRules: false);

        diagnostics.Should().NotContain(d => d.Id == "MM027" || d.Id == "MM028");
        var unique = assembly.GetType("Shelter.Api.ShelterInvariants")!.GetProperty("Unique")!.GetValue(null) as System.Collections.IEnumerable;
        var key = unique!.Cast<object>().Single();
        key.GetType().GetProperty("Schema")!.GetValue(key).Should().Be("Pet");
        ((IEnumerable<string>)key.GetType().GetProperty("Fields")!.GetValue(key)!).Should().Equal("name");
        key.GetType().GetProperty("IgnoreCase")!.GetValue(key).Should().Be(true);
    }

    [Test]
    public void A_key_naming_an_unknown_field_is_reported_and_ignored()
    {
        var spec = Write("unknown.yaml", File.ReadAllText(Shelter).Replace("- fields: [name]", "- fields: [nickname]", StringComparison.Ordinal));

        var (assembly, diagnostics) = GeneratedServer.Build("Shelter", spec, [], string.Empty, withRules: false);

        diagnostics.Should().ContainSingle(d => d.Id == "MM027").Which.GetMessage(null).Should().Contain("unknown fields nickname");
        assembly.GetType("Shelter.Api.ShelterInvariants").Should().BeNull("no valid key is left to describe");
    }

    [Test]
    public void A_create_that_documents_no_refusal_is_reported()
    {
        var spec = Write("no-refusal.yaml", """
            openapi: 3.1.1
            info: { title: Tags, version: 1.0.0 }
            paths:
              /tags:
                post:
                  operationId: createTag
                  requestBody:
                    required: true
                    content: { application/json: { schema: { $ref: '#/components/schemas/Tag' } } }
                  responses:
                    '201':
                      description: Created
                      content: { application/json: { schema: { $ref: '#/components/schemas/Tag' } } }
            components:
              schemas:
                Tag:
                  type: object
                  required: [label]
                  properties:
                    label: { type: string, maxLength: 20 }
                  x-matchmark-unique:
                    - fields: [label]
            """);

        var (_, diagnostics) = GeneratedServer.Build("Tags", spec, [], string.Empty, withRules: false);

        diagnostics.Should().Contain(d => d.Id == "MM028" && d.GetMessage(null).Contains("'createTag'", StringComparison.Ordinal), string.Join("\n", diagnostics));
    }

    [Test]
    public void The_audit_suggests_a_key_where_a_create_documents_409_unless_the_409_is_an_idempotency_key_s()
    {
        var withoutKey = File.ReadAllText(Shelter).Replace("      x-matchmark-unique:\n        - fields: [name]\n          ignoreCase: true\n", string.Empty, StringComparison.Ordinal);
        Audit.Run(SpecJson.Normalize(withoutKey, "shelter.yaml")).Should().Contain(f => f.Rule.Id == "unique-declared" && f.Subject == "createPet");
        Audit.Run(SpecJson.Normalize(File.ReadAllText(Shelter), "shelter.yaml")).Should().NotContain(f => f.Rule.Id == "unique-declared", "the key is declared");

        var orders = Path.GetFullPath(Path.Combine(ThisDirectory(), "..", "samples", "orders", "openapi.yaml"));
        Audit.Run(SpecJson.Normalize(File.ReadAllText(orders), orders)).Should().NotContain(f => f.Rule.Id == "unique-declared",
            "createOrder's 409 is the Idempotency-Key draft's request still being processed");
    }

    private static string Write(string name, string text)
    {
        var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, name);
        File.WriteAllText(path, text);
        return path;
    }

    private static string ThisDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
