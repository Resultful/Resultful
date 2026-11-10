using System.Globalization;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using NUnit.Framework;

namespace Matchmark.Generator.Tests;

/// <summary>
/// Random specs, seeded: each is generated as a contract and server that must compile with no generator failure. The
/// specs vary what broke at scale (GitHub's and Stripe's): parameters named like the generated code's locals, long
/// parameter lists, shared responses and request bodies, enums, nested objects, unions, constraints, several tags. A
/// failing spec is shrunk to the fewest operations and properties that still fail, and printed to commit as a fixture.
/// MATCHMARK_RANDOM_SPECS sets how many seeds run (CI's nightly can run more); the default keeps the suite quick.
/// </summary>
[TestFixture]
public sealed class RandomSpecTests
{
    private static readonly int Count = int.TryParse(Environment.GetEnvironmentVariable("MATCHMARK_RANDOM_SPECS"), out var n) ? n : 12;

    private static readonly string[] Words = ["status", "json", "call", "body", "answer", "rule", "app", "result", "value", "name", "id", "type", "class", "event", "limit", "cursor", "default", "params"];

    private static readonly string[] Scalars = ["string", "integer", "number", "boolean"];

    public static IEnumerable<int> Seeds() => Enumerable.Range(1, Count);

    [TestCaseSource(nameof(Seeds))]
    public void A_random_spec_generates_code_that_compiles(int seed)
    {
        var spec = Random(new Random(seed));
        if (Fails(spec) is { } failure)
        {
            var minimal = Shrink(spec);
            Assert.Fail($"seed {seed}: {failure}\nminimal spec:\n{minimal.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true })}");
        }
    }

    [TestCaseSource(nameof(Seeds))]
    public async Task A_random_spec_s_twin_passes_its_own_suite(int seed)
    {
        // The spec, exercised as well as compiled: its generated suite against the twin made from it alone. A failure is
        // the engine and the twin disagreeing about the same document, which is a bug in one of them.
        var json = Matchmark.Generator.SpecJson.Normalize(Random(new Random(seed)).ToJsonString(), "random.json");
        if (Environment.GetEnvironmentVariable("MATCHMARK_RANDOM_DUMP") is { } dump)
        {
            File.WriteAllText(Path.Combine(dump, $"random-{seed}.json"), json);
        }

        var suite = Matchmark.Testing.RegressionSuite.FromJson(json);
        using var client = new HttpClient(new Matchmark.Testing.SpecTwin(json)) { BaseAddress = new Uri("http://twin/") };

        var report = await suite.RunAsync(client, new Matchmark.Testing.RegressionOptions());

        TestContext.Out.WriteLine($"seed {seed}: {report.Cases.Count} cases");
        report.Cases.Should().NotBeEmpty("the suite examined the spec");
        report.Failures.Should().BeEmpty($"seed {seed}:\n{report}");
    }

    /// <summary>A small, valid spec: 1 to 4 tags, a few operations each, components shared between them.</summary>
    private static JsonObject Random(Random random)
    {
        string Word() => Words[random.Next(Words.Length)];
        var schemas = new JsonObject();
        var paths = new JsonObject();

        JsonObject Scalar()
        {
            var type = Scalars[random.Next(Scalars.Length)];
            var schema = new JsonObject { ["type"] = type };
            switch (type)
            {
                case "string" when random.Next(3) == 0:
                    schema["enum"] = new JsonArray(Enumerable.Range(0, random.Next(1, 4)).Select(i => (JsonNode?)$"{Word()}{i}").ToArray());
                    break;
                case "string":
                    schema["maxLength"] = random.Next(1, 50);
                    break;
                case "integer":
                    schema["minimum"] = 0;
                    schema["maximum"] = random.Next(1, 1000);
                    break;
            }

            return schema;
        }

        // Component schemas: objects of scalars, some nesting another, one union.
        var names = new List<string>();
        for (var i = 0; i < random.Next(1, 4); i++)
        {
            var name = $"Thing{i}";
            var properties = new JsonObject();
            for (var p = 0; p < random.Next(1, 6); p++)
            {
                properties[$"{Word()}{p}"] = p == 0 && names.Count > 0 && random.Next(2) == 0 ? new JsonObject { ["$ref"] = $"#/components/schemas/{names[^1]}" } : Scalar();
            }

            schemas[name] = new JsonObject { ["type"] = "object", ["required"] = new JsonArray(properties.First().Key), ["properties"] = properties };
            names.Add(name);
        }

        if (names.Count >= 2 && random.Next(2) == 0)
        {
            // A union whose members are disjoint, each with its own required kind: members that overlap (one's every value
            // also another's) make a oneOf no value satisfies, which is the spec's fault, not the engine's or the twin's.
            foreach (var (member, kind) in new[] { ("EitherA", "a"), ("EitherB", "b") })
            {
                schemas[member] = new JsonObject
                {
                    ["type"] = "object",
                    ["required"] = new JsonArray("kind"),
                    ["properties"] = new JsonObject { ["kind"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(kind) }, [$"{Word()}1"] = Scalar() },
                };
            }

            schemas["Either"] = new JsonObject
            {
                ["oneOf"] = new JsonArray(new[] { "EitherA", "EitherB" }.Select(n => (JsonNode?)new JsonObject { ["$ref"] = $"#/components/schemas/{n}" }).ToArray()),
            };
        }

        var responses = new JsonObject
        {
            ["Problem"] = new JsonObject
            {
                ["description"] = "A problem",
                ["content"] = new JsonObject { ["application/json"] = new JsonObject { ["schema"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["title"] = new JsonObject { ["type"] = "string" } } } } },
            },
        };
        var requestBodies = new JsonObject
        {
            ["Change"] = new JsonObject
            {
                ["required"] = true,
                ["content"] = new JsonObject { ["application/json"] = new JsonObject { ["schema"] = new JsonObject { ["$ref"] = $"#/components/schemas/{names[0]}" } } },
            },
        };

        for (var t = 0; t < random.Next(1, 5); t++)
        {
            var tag = $"{Word()}s{t}";
            for (var o = 0; o < random.Next(1, 4); o++)
            {
                var operation = new JsonObject { ["operationId"] = $"op{t}x{o}", ["tags"] = new JsonArray(tag) };
                // Parameters, some named like the generated code's locals; sometimes more than Func takes.
                var parameters = new JsonArray();
                for (var p = 0; p < (random.Next(6) == 0 ? 18 : random.Next(0, 4)); p++)
                {
                    parameters.Add(new JsonObject { ["name"] = $"{Word()}{(p > 0 ? p.ToString(CultureInfo.InvariantCulture) : string.Empty)}", ["in"] = "query", ["schema"] = Scalar() });
                }

                parameters.Add(new JsonObject { ["name"] = "itemId", ["in"] = "path", ["required"] = true, ["schema"] = new JsonObject { ["type"] = "integer" } });
                operation["parameters"] = parameters;
                var method = new[] { "get", "put", "post", "delete" }[o % 4];
                if (method is "put" or "post")
                {
                    operation["requestBody"] = random.Next(2) == 0
                        ? new JsonObject { ["$ref"] = "#/components/requestBodies/Change" }
                        : new JsonObject { ["required"] = true, ["content"] = new JsonObject { ["application/json"] = new JsonObject { ["schema"] = new JsonObject { ["$ref"] = $"#/components/schemas/{names[random.Next(names.Count)]}" } } } };
                }

                var success = new JsonObject { ["description"] = "OK" };
                if (method != "delete")
                {
                    success["content"] = new JsonObject { ["application/json"] = new JsonObject { ["schema"] = new JsonObject { ["$ref"] = $"#/components/schemas/{(schemas.ContainsKey("Either") && random.Next(3) == 0 ? "Either" : names[random.Next(names.Count)])}" } } };
                }

                operation["responses"] = new JsonObject
                {
                    [method == "post" ? "201" : method == "delete" ? "204" : "200"] = success,
                    ["404"] = new JsonObject { ["$ref"] = "#/components/responses/Problem" },
                    ["503"] = new JsonObject { ["$ref"] = "#/components/responses/Problem" },
                };
                var path = $"/{tag}/x{o}/{{itemId}}";
                (paths[path] as JsonObject ?? (JsonObject)(paths[path] = new JsonObject()))[method] = operation;
            }
        }

        return new JsonObject
        {
            ["openapi"] = "3.1.1",
            ["info"] = new JsonObject { ["title"] = "Random", ["version"] = "1.0.0" },
            ["paths"] = paths,
            ["components"] = new JsonObject { ["schemas"] = schemas, ["responses"] = responses, ["requestBodies"] = requestBodies },
        };
    }

    /// <summary>What's wrong with the code a spec generates, or null when it compiles and the generator didn't fail.</summary>
    private static string? Fails(JsonObject spec)
    {
        var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"random-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, spec.ToJsonString());
        try
        {
            var (_, diagnostics) = GeneratedServer.Build("Random", path, [], string.Empty, withRules: false);
            return diagnostics.FirstOrDefault(d => d.Id == "CS8785") is { } crashed ? crashed.GetMessage(CultureInfo.InvariantCulture) : null;
        }
        catch (AssertionException e)
        {
            return e.Message.Split('\n')[0];
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>The spec with operations, then properties, then parameters removed one at a time while it still fails.</summary>
    private static JsonObject Shrink(JsonObject spec)
    {
        var current = (JsonObject)spec.DeepClone();
        bool Try(Func<JsonObject, bool> remove)
        {
            var candidate = (JsonObject)current.DeepClone();
            if (remove(candidate) && Fails(candidate) is not null)
            {
                current = candidate;
                return true;
            }

            return false;
        }

        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var path in current["paths"]!.AsObject().Select(p => p.Key).ToList())
            {
                foreach (var method in current["paths"]![path]!.AsObject().Select(m => m.Key).ToList())
                {
                    changed |= Try(c =>
                    {
                        var item = c["paths"]![path]!.AsObject();
                        item.Remove(method);
                        if (item.Count == 0)
                        {
                            c["paths"]!.AsObject().Remove(path);
                        }

                        return c["paths"]!.AsObject().Count > 0;
                    });
                }
            }

            foreach (var operation in current["paths"]!.AsObject().SelectMany(p => p.Value!.AsObject()).Select(m => m.Value).OfType<JsonObject>().ToList())
            {
                var id = operation["operationId"]!.GetValue<string>();
                var count = (operation["parameters"] as JsonArray)?.Count ?? 0;
                for (var i = count - 2; i >= 0; i--)
                {
                    var index = i;
                    changed |= Try(c =>
                    {
                        var copy = c["paths"]!.AsObject().SelectMany(p => p.Value!.AsObject()).Select(m => m.Value).OfType<JsonObject>().First(o => o["operationId"]!.GetValue<string>() == id);
                        (copy["parameters"] as JsonArray)!.RemoveAt(index);
                        return true;
                    });
                }
            }
        }

        return current;
    }
}
