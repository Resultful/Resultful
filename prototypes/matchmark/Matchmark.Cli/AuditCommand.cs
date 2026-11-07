using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Matchmark.Generator;
using Matchmark.Testing;

namespace Matchmark.Cli;

/// <summary>
/// <c>audit &lt;spec&gt;</c>: what the spec leaves out, each finding measured in tests. The suite is generated from the spec,
/// then again with each finding's change made, so the report says what each change adds.
/// </summary>
public static class AuditCommand
{
    public const string Usage = """
        usage: matchmark audit <spec> [--arazzo <file>]... [--overlay <file>]... [--sarif <file>] [--spectral <file>] [--strict]

          Reports what the spec leaves out that the generated suite, fake and server would use, and measures what each
          change would add to the suite by generating it again with the change made.
          --arazzo    an Arazzo document whose workflows count (repeat for more)
          --overlay   an OpenAPI Overlay applied to the spec first (repeat for more)
          --sarif     also write the findings as SARIF 2.1.0, for CI and editors
          --spectral  also write the static rules as a Spectral ruleset
          --strict    exit 1 when there is a warning
        """;

    /// <summary>A finding, with what its change adds to the suite.</summary>
    public sealed record Measured(AuditFinding Finding, int Cases, int Workflows, string Unlocks);

    public sealed record Result(IReadOnlyList<Measured> Findings, int Cases, int Workflows)
    {
        /// <summary>The share of what the suite could check, with every change made, that it checks now.</summary>
        public double Score => Cases + Workflows + Findings.Sum(f => f.Cases + f.Workflows) is var possible and > 0
            ? (double)(Cases + Workflows) / possible
            : 1;
    }

    public static async Task<int> RunAsync(string[] args, TextWriter output, CancellationToken cancellationToken = default)
    {
        if (args.Length < 1 || args[0].StartsWith("--", StringComparison.Ordinal))
        {
            await output.WriteLineAsync(Usage);
            return 2;
        }

        string specJson;
        var arazzo = new List<string>();
        try
        {
            var overlays = new List<(string, string)>();
            for (var i = 1; i < args.Length - 1; i++)
            {
                if (args[i] == "--arazzo")
                {
                    arazzo.Add(SpecJson.Arazzo(await File.ReadAllTextAsync(args[i + 1], cancellationToken), args[i + 1]));
                }
                else if (args[i] == "--overlay")
                {
                    overlays.Add((await File.ReadAllTextAsync(args[i + 1], cancellationToken), args[i + 1]));
                }
            }

            specJson = SpecJson.Normalize(await File.ReadAllTextAsync(args[0], cancellationToken), args[0], out var problems, overlays);
            foreach (var problem in problems)
            {
                await output.WriteLineAsync($"warning: the reader objected, and that part may be missing: {problem}");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or SharpYaml.YamlException or JsonException)
        {
            await output.WriteLineAsync($"error: {e.Message}");
            return 2;
        }

        var result = Measure(specJson, [.. arazzo]);
        await output.WriteLineAsync(Text(result));
        if (Option(args, "--sarif") is { } sarif)
        {
            await File.WriteAllTextAsync(sarif, Sarif(result, args[0]), cancellationToken);
        }

        if (Option(args, "--spectral") is { } spectral)
        {
            await File.WriteAllTextAsync(spectral, Spectral(), cancellationToken);
        }

        return args.Contains("--strict") && result.Findings.Any(f => f.Finding.Rule.Level == "warning") ? 1 : 0;
    }

    private static string? Option(string[] args, string name)
        => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;

    /// <summary>The findings, each measured: the suite generated again with its change made.</summary>
    public static Result Measure(string specJson, params string[] arazzo)
    {
        var suite = RegressionSuite.FromJson(specJson, arazzo);
        var spec = JsonNode.Parse(specJson)!.AsObject();
        var measured = new List<Measured>();
        foreach (var finding in Audit.Run(specJson, arazzo))
        {
            var (cases, workflows) = (0, 0);
            if (finding.Fixed(spec) is { } changed)
            {
                try
                {
                    var after = RegressionSuite.FromJson(changed.ToJsonString(), arazzo);
                    // What it adds, by name: a change can also retire a case (a read-only id is no longer sent with a wrong type).
                    cases = after.Cases.Select(c => $"{c.Operation.Id} {c.Name}").Except(suite.Cases.Select(c => $"{c.Operation.Id} {c.Name}")).Count();
                    workflows = after.Workflows.Select(w => w.ToString()).Except(suite.Workflows.Select(w => w.ToString())).Count();
                }
                catch (Exception e) when (e is InvalidOperationException or KeyNotFoundException or FormatException)
                {
                    // A change the engine can't read is measured as nothing.
                }
            }
            else if (finding.Rule == Audit.RequestExample)
            {
                cases = 1;
            }

            measured.Add(new Measured(finding, cases, workflows, Unlocks(finding, suite, cases, workflows)));
        }

        return new Result(measured, suite.Cases.Count, suite.Workflows.Count);
    }

    private static string Unlocks(AuditFinding finding, RegressionSuite suite, int cases, int workflows)
    {
        var added = new List<string>();
        if (cases > 0)
        {
            added.Add(cases == 1 ? "1 case" : $"{cases} cases");
        }

        if (workflows > 0)
        {
            added.Add(workflows == 1 ? "1 workflow" : $"{workflows} workflows");
        }

        if (finding.Rule == Audit.RejectionDocumented && finding.Message.Contains("400", StringComparison.Ordinal)
            && suite.Cases.Count(c => c.Operation.Id == (finding.OperationId ?? finding.Subject) && c.Kind == CaseKind.Invalid) is var invalid and > 0)
        {
            return $"{invalid} invalid {(invalid == 1 ? "case ends" : "cases end")} in a warning, not a check. Document the 400.";
        }

        return added.Count > 0 ? $"Saying it adds {string.Join(" and ", added)}." : $"Unlocks {finding.Rule.Unlocks}.";
    }

    public static string Text(Result result)
    {
        var text = new StringBuilder();
        var width = result.Findings.Select(f => f.Finding.Subject.Length).DefaultIfEmpty(0).Max();
        foreach (var group in result.Findings.GroupBy(f => f.Finding.Rule))
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"{group.Key.Id} ({group.Key.Level}): {group.Key.Title}");
            foreach (var m in group)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"  {m.Finding.Subject.PadRight(width)}  {m.Finding.Message}");
                text.AppendLine(CultureInfo.InvariantCulture, $"  {new string(' ', width)}  → {m.Unlocks}");
            }
        }

        var cases = result.Findings.Sum(f => f.Cases);
        var workflows = result.Findings.Sum(f => f.Workflows);
        text.Append(CultureInfo.InvariantCulture,
            $"Score: {result.Score * 100:0}% of what the suite could check is checkable ({result.Cases} cases, {result.Workflows} workflows). ");
        text.Append(CultureInfo.InvariantCulture,
            $"{result.Findings.Count} findings; the changes above would add {cases} cases and {workflows} workflows.");
        return text.ToString();
    }

    /// <summary>SARIF 2.1.0: one run, the audit's rules, a result per finding at the spec with its JSON Pointer.</summary>
    public static string Sarif(Result result, string specPath)
    {
        var rules = new JsonArray(Audit.Rules.Select(r => (JsonNode)new JsonObject
        {
            ["id"] = r.Id,
            ["shortDescription"] = new JsonObject { ["text"] = r.Title },
            ["fullDescription"] = new JsonObject { ["text"] = $"{r.Title}: it unlocks {r.Unlocks}." },
            ["defaultConfiguration"] = new JsonObject { ["level"] = r.Level },
        }).ToArray());
        var results = new JsonArray(result.Findings.Select(m => (JsonNode)new JsonObject
        {
            ["ruleId"] = m.Finding.Rule.Id,
            ["level"] = m.Finding.Rule.Level,
            ["message"] = new JsonObject { ["text"] = $"{m.Finding.Subject} {m.Finding.Message}. {m.Unlocks}" },
            ["locations"] = new JsonArray(new JsonObject
            {
                ["physicalLocation"] = new JsonObject { ["artifactLocation"] = new JsonObject { ["uri"] = specPath.Replace('\\', '/') } },
                ["logicalLocations"] = new JsonArray(new JsonObject { ["fullyQualifiedName"] = "#" + m.Finding.Location, ["kind"] = "element" }),
            }),
            ["properties"] = new JsonObject { ["casesAdded"] = m.Cases, ["workflowsAdded"] = m.Workflows },
        }).ToArray());
        var log = new JsonObject
        {
            ["$schema"] = "https://json.schemastore.org/sarif-2.1.0.json",
            ["version"] = "2.1.0",
            ["runs"] = new JsonArray(new JsonObject
            {
                ["tool"] = new JsonObject { ["driver"] = new JsonObject { ["name"] = "matchmark audit", ["informationUri"] = "https://github.com/Resultful/Resultful", ["rules"] = rules } },
                ["results"] = results,
            }),
        };
        return log.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// The static part of the audit as a Spectral ruleset, for teams that keep their linter. What only Matchmark can do,
    /// measuring each finding in cases, stays in <c>matchmark audit</c>.
    /// </summary>
    public static string Spectral() => """
        # Matchmark's completeness rules, as far as Spectral can say them. `matchmark audit` also measures what each
        # finding is worth in generated cases; this ruleset only finds them.
        formats: [oas3]
        rules:
          matchmark-operation-id:
            description: Every operation has an operationId, so it is in the contract and links and workflows can name it.
            severity: warn
            given: $.paths[*][get,put,post,delete,options,head,patch,trace,query]
            then: { field: operationId, function: truthy }
          matchmark-bounded-string:
            description: Every string has maxLength, pattern, format or enum, for boundary and too-long cases.
            severity: warn
            given: $..[?(@ && @.type == 'string' && !@.enum && !@.const && !@.readOnly)]
            then:
              function: schema
              functionOptions:
                schema: { anyOf: [{ required: [maxLength] }, { required: [pattern] }, { required: [format] }] }
          matchmark-bounded-number:
            description: Every number has a minimum and a maximum, for boundary and out-of-range cases.
            severity: warn
            given: $..[?(@ && (@.type == 'integer' || @.type == 'number') && !@.enum && !@.const && !@.readOnly)]
            then:
              function: schema
              functionOptions:
                schema:
                  allOf:
                    - anyOf: [{ required: [minimum] }, { required: [exclusiveMinimum] }]
                    - anyOf: [{ required: [maximum] }, { required: [exclusiveMaximum] }]
          matchmark-rejection-documented:
            description: An operation that takes input documents how it rejects it (400, 4XX or default).
            severity: warn
            given: $.paths[*][put,post,patch,query].responses
            then:
              function: schema
              functionOptions:
                schema: { anyOf: [{ required: ['400'] }, { required: ['4XX'] }, { required: ['422'] }, { required: [default] }] }
          matchmark-additional-properties:
            description: Objects state additionalProperties, so the suite knows whether an extra field is a bug.
            severity: info
            given: $..[?(@ && @.properties && @.type == 'object')]
            then:
              function: schema
              functionOptions:
                schema: { anyOf: [{ required: [additionalProperties] }, { required: [unevaluatedProperties] }] }
          matchmark-read-only:
            description: Server-assigned fields are readOnly.
            severity: warn
            given: $..properties[id,createdAt,created_at,updatedAt,updated_at]
            then: { field: readOnly, function: truthy }
          matchmark-create-linked:
            description: A create links to reading what it created (or an Arazzo workflow reaches it).
            severity: warn
            given: $.paths[*].post.responses['201']
            then: { field: links, function: truthy }
          matchmark-idempotency-key:
            description: An unsafe create takes an Idempotency-Key header.
            severity: info
            given: $.paths[*].post[?(@.responses && @.responses['201'])]
            then:
              field: parameters
              function: schema
              functionOptions:
                schema: { type: array, contains: { properties: { in: { const: header }, name: { pattern: '^[Ii]dempotency-[Kk]ey$' } }, required: [in, name] } }
        """;
}
