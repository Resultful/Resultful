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

/// <summary>The references trait in the generator (docs/reference/invariants.md#references): metadata, and MM027 for a wrong declaration.</summary>
[TestFixture]
public sealed class ReferenceTraitTests
{
    private static readonly string Ledger = Path.GetFullPath(Path.Combine(Directory(), "..", "samples", "ledger"));

    [Test]
    public void The_contract_carries_the_ledger_s_references_as_data()
    {
        var (assembly, diagnostics) = GeneratedServer.Build("Ledger", Path.Combine(Ledger, "openapi.yaml"), [Path.Combine(Ledger, "ledger.overlay.yaml")], string.Empty, withRules: true);

        diagnostics.Should().NotContain(d => d.Id == "MM027" || d.Id == "MM028", string.Join("\n", diagnostics));
        var references = ((System.Collections.IEnumerable)assembly.GetType("Ledger.Api.LedgerInvariants")!.GetProperty("References")!.GetValue(null)!).Cast<object>()
            .Select(r => $"{r.GetType().GetProperty("Schema")!.GetValue(r)}.{r.GetType().GetProperty("Field")!.GetValue(r)} → {r.GetType().GetProperty("Target")!.GetValue(r)} ({r.GetType().GetProperty("OnDelete")!.GetValue(r)})");
        references.Should().Equal("Transfer.from → /accounts/{accountId} (restrict)", "Transfer.to → /accounts/{accountId} (restrict)");
    }

    [TestCase("target: '/accounts/{accountId}'", "from: { target: '/owners/{ownerId}' }", "a target path the spec doesn't have")]
    [TestCase("from: { target: '/accounts/{accountId}', status: 400 }", "payer: { target: '/accounts/{accountId}' }", "an unknown field payer")]
    [TestCase("from: { target: '/accounts/{accountId}', status: 400 }", "from: { target: '/accounts/{accountId}', onDelete: clear }", "onDelete clear on from, which can't be null")]
    public void A_wrong_reference_is_reported(string _, string replacement, string expected)
    {
        var overlay = File.ReadAllText(Path.Combine(Ledger, "ledger.overlay.yaml"))
            .Replace("from: { target: '/accounts/{accountId}', status: 400 }", replacement, StringComparison.Ordinal);
        var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"wrong-{Math.Abs(replacement.GetHashCode(StringComparison.Ordinal))}.overlay.yaml");
        File.WriteAllText(path, overlay.Replace("extends: ./openapi.yaml", "extends: " + Path.Combine(Ledger, "openapi.yaml"), StringComparison.Ordinal));

        var (_, diagnostics) = GeneratedServer.Build("Ledger", Path.Combine(Ledger, "openapi.yaml"), [path], string.Empty, withRules: true);

        diagnostics.Should().Contain(d => d.Id == "MM027" && d.GetMessage(null).Contains(expected, StringComparison.Ordinal), string.Join("\n", diagnostics));
    }

    private static string Directory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}

/// <summary>The derived and order traits in the generator: MM027 for what they name that isn't there.</summary>
[TestFixture]
public sealed class DerivedAndOrderTraitTests
{
    private static readonly string Ledger = Path.GetFullPath(Path.Combine(Directory(), "..", "samples", "ledger"));
    private static readonly string PetStore = Path.GetFullPath(Path.Combine(Directory(), "..", "Matchmark.Sample.Contract", "petstore.yaml"));

    [Test]
    public void The_samples_declarations_are_right()
    {
        GeneratedServer.Build("Ledger", Path.Combine(Ledger, "openapi.yaml"), [Path.Combine(Ledger, "ledger.overlay.yaml")], string.Empty, withRules: true)
            .Diagnostics.Should().NotContain(d => d.Id == "MM027");
        GeneratedServer.Build("PetStore", PetStore, [], string.Empty, withRules: false).Diagnostics.Should().NotContain(d => d.Id == "MM027");
    }

    [Test]
    public void A_derived_field_that_isn_t_there_is_reported()
    {
        var overlay = File.ReadAllText(Path.Combine(Ledger, "ledger.overlay.yaml"))
            .Replace("balance: { on: create, value: request.opening }", "credit: { on: create, value: request.opening }", StringComparison.Ordinal)
            .Replace("extends: ./openapi.yaml", "extends: " + Path.Combine(Ledger, "openapi.yaml"), StringComparison.Ordinal);
        var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "derived-wrong.overlay.yaml");
        File.WriteAllText(path, overlay);

        GeneratedServer.Build("Ledger", Path.Combine(Ledger, "openapi.yaml"), [path], string.Empty, withRules: true).Diagnostics
            .Should().Contain(d => d.Id == "MM027" && d.GetMessage(null).Contains("x-matchmark-derived", StringComparison.Ordinal) && d.GetMessage(null).Contains("unknown field credit", StringComparison.Ordinal));
    }

    [Test]
    public void An_order_by_a_field_the_items_don_t_have_is_reported()
    {
        var spec = Path.Combine(TestContext.CurrentContext.WorkDirectory, "order-wrong.yaml");
        File.WriteAllText(spec, File.ReadAllText(PetStore).Replace("by: [id]", "by: [age]", StringComparison.Ordinal));

        GeneratedServer.Build("PetStore", spec, [], string.Empty, withRules: false).Diagnostics
            .Should().Contain(d => d.Id == "MM027" && d.GetMessage(null).Contains("listPets's x-matchmark-order", StringComparison.Ordinal) && d.GetMessage(null).Contains("unknown field age", StringComparison.Ordinal));
    }

    private static string Directory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}

/// <summary>The limit trait in the generator: MM027 for a limit declared wrongly, MM028 for a create with no status to refuse with.</summary>
[TestFixture]
public sealed class LimitTraitTests
{
    private static readonly string PetStore = Path.GetFullPath(Path.Combine(Directory(), "..", "Matchmark.Sample.Contract", "petstore.yaml"));

    [TestCase("{ max: 100 }", null)]
    [TestCase("{ max: 0 }", "a max that isn't a whole number of at least 1")]
    [TestCase("{ max: 5, scope: tenant }", "an unknown scope tenant")]
    public void A_limit_is_read_and_checked(string limit, string? wrong)
    {
        var diagnostics = Build(limit);
        if (wrong is null)
        {
            diagnostics.Should().NotContain(d => d.Id == "MM027" && d.GetMessage(null).Contains("x-matchmark-limit", StringComparison.Ordinal));
        }
        else
        {
            diagnostics.Should().Contain(d => d.Id == "MM027" && d.GetMessage(null).Contains("Pet's x-matchmark-limit", StringComparison.Ordinal) && d.GetMessage(null).Contains(wrong, StringComparison.Ordinal));
        }
    }

    [Test]
    public void A_create_with_no_status_for_the_limit_is_reported_unless_it_names_one()
    {
        // The Pet Store's create documents 409 (for the unique name), so the limit has a status; this spec's doesn't.
        const string Spec = """
            {
              "openapi": "3.1.1",
              "info": { "title": "Pins", "version": "1.0.0" },
              "paths": {
                "/pins": { "post": { "operationId": "pin",
                  "requestBody": { "required": true, "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Pin" } } } },
                  "responses": { "201": { "description": "Pinned", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Pin" } } } } } } }
              },
              "components": { "schemas": { "Pin": { "type": "object", "properties": { "note": { "type": "string" } }, "x-matchmark-limit": { "max": 3 } } } }
            }
            """;
        var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "pins.json");
        File.WriteAllText(path, Spec);
        GeneratedServer.Build("Pins", path, [], string.Empty, withRules: false).Diagnostics
            .Should().Contain(d => d.Id == "MM028" && d.GetMessage(null).Contains("Pin's limit", StringComparison.Ordinal));

        File.WriteAllText(path, Spec.Replace("{ \"max\": 3 }", "{ \"max\": 3, \"status\": 403 }", StringComparison.Ordinal));
        GeneratedServer.Build("Pins", path, [], string.Empty, withRules: false).Diagnostics.Should().NotContain(d => d.Id == "MM028");
    }

    [Test]
    public void The_audit_notes_a_limit_too_high_to_reach()
    {
        string Spec(int max) => SpecJson.Normalize(File.ReadAllText(PetStore).Replace("      x-matchmark-unique:\n", $"      x-matchmark-limit: {{ max: {max} }}\n      x-matchmark-unique:\n", StringComparison.Ordinal), "petstore.yaml");
        Audit.Run(Spec(500)).Should().Contain(f => f.Rule.Id == "limit-exercised" && f.Subject == "Pet");
        Audit.Run(Spec(50)).Should().NotContain(f => f.Rule.Id == "limit-exercised");
    }

    private static IReadOnlyList<Microsoft.CodeAnalysis.Diagnostic> Build(string limit)
    {
        var spec = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"limit-{Math.Abs(limit.GetHashCode(StringComparison.Ordinal))}.yaml");
        File.WriteAllText(spec, File.ReadAllText(PetStore).Replace("      x-matchmark-unique:\n", $"      x-matchmark-limit: {limit}\n      x-matchmark-unique:\n", StringComparison.Ordinal));
        return GeneratedServer.Build("PetStore", spec, [], string.Empty, withRules: false).Diagnostics;
    }

    private static string Directory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}

/// <summary>The conserved trait in the generator: the Ledger's declaration is right, and MM027 reports one that isn't.</summary>
[TestFixture]
public sealed class ConservedTraitTests
{
    private static readonly string Ledger = Path.GetFullPath(Path.Combine(Directory(), "..", "samples", "ledger"));

    [TestCase("total: balance", "except: [openAccount]", null)]
    [TestCase("total: credit", "except: [openAccount]", "an unknown field credit")]
    [TestCase("total: owner", "except: [closeAccount]", "an unknown operation closeAccount in except")]
    public void A_conserved_total_is_checked(string total, string except, string? wrong)
    {
        var overlay = File.ReadAllText(Path.Combine(Ledger, "ledger.overlay.yaml"))
            .Replace("- total: balance", "- " + total, StringComparison.Ordinal)
            .Replace("except: [openAccount]", except, StringComparison.Ordinal)
            .Replace("extends: ./openapi.yaml", "extends: " + Path.Combine(Ledger, "openapi.yaml"), StringComparison.Ordinal);
        var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"conserved-{Math.Abs((total + except).GetHashCode(StringComparison.Ordinal))}.overlay.yaml");
        File.WriteAllText(path, overlay);

        var diagnostics = GeneratedServer.Build("Ledger", Path.Combine(Ledger, "openapi.yaml"), [path], string.Empty, withRules: true).Diagnostics
            .Where(d => d.Id == "MM027" && d.GetMessage(null).Contains("x-matchmark-conserved", StringComparison.Ordinal)).ToList();
        if (wrong is null)
        {
            diagnostics.Should().BeEmpty();
        }
        else
        {
            diagnostics.Should().Contain(d => d.GetMessage(null).Contains(wrong, StringComparison.Ordinal));
        }
    }

    private static string Directory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}

/// <summary>The scoped trait in the generator: MM027 for a scope declared wrongly, MM028 for a read with no status for it.</summary>
[TestFixture]
public sealed class ScopedTraitTests
{
    private static readonly string Sessions = Path.GetFullPath(Path.Combine(Directory(), "..", "samples", "sessions", "openapi.yaml"));

    [TestCase("{ by: principal }", null, null)]
    [TestCase("{ by: tenant }", "MM027", "an unknown by tenant")]
    [TestCase("{ by: principal, visible: everyone }", "MM027", "an unknown visible everyone")]
    public void A_scope_is_checked(string scoped, string? id, string? wrong)
    {
        var diagnostics = Build(scoped);
        if (id is null)
        {
            diagnostics.Should().NotContain(d => d.GetMessage(null).Contains("scope", StringComparison.Ordinal) && (d.Id == "MM027" || d.Id == "MM028"));
        }
        else
        {
            diagnostics.Should().Contain(d => d.Id == id && d.GetMessage(null).Contains(wrong!, StringComparison.Ordinal));
        }
    }

    [Test]
    public void A_read_with_no_status_for_the_scope_is_reported()
        => Build("{ by: principal }", withoutNotFound: true).Should().Contain(d => d.Id == "MM028" && d.GetMessage(null).Contains("Pet's scope", StringComparison.Ordinal));

    private static IReadOnlyList<Microsoft.CodeAnalysis.Diagnostic> Build(string scoped, bool withoutNotFound = false)
    {
        var text = File.ReadAllText(Sessions).Replace("    Pet:\n      type: object\n", $"    Pet:\n      type: object\n      x-matchmark-scoped: {scoped}\n", StringComparison.Ordinal);
        if (withoutNotFound)
        {
            text = text.Replace("        '404':\n          description: No such pet\n", string.Empty, StringComparison.Ordinal);
        }

        var spec = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"scoped-{Math.Abs((scoped + withoutNotFound).GetHashCode(StringComparison.Ordinal))}.yaml");
        File.WriteAllText(spec, text);
        return GeneratedServer.Build("Shelter", spec, [], string.Empty, withRules: false).Diagnostics;
    }

    private static string Directory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
