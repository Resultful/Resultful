using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Matchmark.Rules;

namespace Matchmark.Testing;

/// <summary>
/// Random valid requests on top of the deterministic cases (<see cref="RegressionOptions.Random"/>): bodies and optional
/// parameters drawn from their schemas with a fixed seed, kept to the declared rules and dependencies, each checked as a
/// valid case. A failure is shrunk to the smallest request that still fails the same way: optional properties left
/// out, strings and arrays shortened, numbers moved toward their minimum.
/// </summary>
internal sealed partial class RegressionRunner
{
    private const int ShrinkLimit = 40;

    private async Task<List<CaseResult>> RandomAsync(CancellationToken cancellationToken)
    {
        var results = new List<CaseResult>();
        var random = new Random(options.Seed);
        foreach (var operation in suite.Spec.Operations)
        {
            // The template: the operation's simplest valid case, for its path and required values.
            if (suite.Cases.FirstOrDefault(c => c.Operation == operation && c.Kind == CaseKind.Valid) is not { } template)
            {
                continue;
            }

            for (var i = 1; i <= options.Random; i++)
            {
                if (Draw(operation, template, random, i) is not { } @case)
                {
                    continue;
                }

                var result = await RunCaseAsync(@case, cancellationToken).ConfigureAwait(false);
                results.Add(result.Findings.Any(f => f.Severity == Severity.Failure)
                    ? await ShrinkAsync(result, cancellationToken).ConfigureAwait(false)
                    : result);
            }
        }

        return results;
    }

    /// <summary>A random valid request: the template's, with a random body and random optional query values.</summary>
    private RegressionCase? Draw(SpecOperation operation, RegressionCase template, Random random, int n)
    {
        var request = template.Request;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            string? body = request.Body;
            if (operation.Body is { } schema && OpenApiSpec.IsJson(schema.MediaType))
            {
                if (Value(schema.Schema, random, 0) is not { } value || !suite.Validator.IsValid(value, schema.Schema, Direction.Request)
                    || SchemaRules.Broken(suite.Spec, value, schema.Schema).Any(b => b.Error is null))
                {
                    continue;
                }

                body = value.ToJsonString();
            }

            var query = request.Query.ToList();
            foreach (var parameter in operation.Parameters.Where(p => p.In == "query" && !p.Required && query.All(q => q.Key != p.Name)))
            {
                if (random.Next(2) == 0 && Value(parameter.Schema, random, 0) is JsonValue value && suite.Validator.IsValid(value, parameter.Schema, Direction.Request))
                {
                    query.Add(new(parameter.Name, value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : value.ToJsonString()));
                }
            }

            var given = query.GroupBy(q => q.Key).ToDictionary(g => g.Key, g => g.First().Value, StringComparer.Ordinal);
            if (operation.Dependencies.Any(d => !d.Holds(given)))
            {
                continue;
            }

            return template with
            {
                Name = $"{operation.Id}: random {n} (seed {options.Seed})",
                Request = request with { Query = query, Body = body },
            };
        }

        return null;
    }

    /// <summary>A random value of a schema: within its bounds, optional properties present half the time.</summary>
    private JsonNode? Value(JsonNode schemaNode, Random random, int depth)
    {
        var schema = suite.Spec.Resolve(schemaNode);
        if (depth > 6)
        {
            return suite.Samples.TrySample(schemaNode, false, out var fallback) ? fallback : null;
        }

        if (schema["enum"] is JsonArray values && values.Count > 0)
        {
            return values[random.Next(values.Count)]?.DeepClone();
        }

        if (schema["const"] is { } constant)
        {
            return constant.DeepClone();
        }

        foreach (var keyword in new[] { "oneOf", "anyOf" })
        {
            if (schema[keyword] is JsonArray { Count: > 0 } members)
            {
                // A union is sampled as the deterministic cases sample it (discriminator and all): only its members vary.
                return suite.Samples.TrySample(members[random.Next(members.Count)]!, true, out var member) ? member : null;
            }
        }

        if (schema["allOf"] is JsonArray)
        {
            return suite.Samples.TrySample(schemaNode, random.Next(2) == 0, out var whole) ? whole : null;
        }

        var types = SchemaValidator.Types(schema).Where(t => t != "null").ToList();
        if (SchemaValidator.Types(schema).Contains("null") && random.Next(10) == 0)
        {
            return null;
        }

        switch (types.FirstOrDefault())
        {
            case "object" or null when schema["properties"] is JsonObject properties:
                var required = (schema["required"] as JsonArray ?? []).Select(r => r!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
                var obj = new JsonObject();
                foreach (var (name, property) in properties)
                {
                    if (property is null || suite.Validator.Flag(property, "readOnly") || !required.Contains(name) && random.Next(2) == 0)
                    {
                        continue;
                    }

                    obj[name] = Value(property, random, depth + 1);
                }

                return obj;
            case "array":
                var min = (int)(SchemaValidator.Number(schema, "minItems") ?? 0);
                var max = (int)Math.Min(SchemaValidator.Number(schema, "maxItems") ?? min + 3, min + 3);
                var array = new JsonArray();
                for (var i = random.Next(min, max + 1); i > 0 && schema["items"] is { } items; i--)
                {
                    array.Add(Value(items, random, depth + 1));
                }

                return array;
            case "integer":
                return JsonValue.Create((long)Number(schema, random, integer: true));
            case "number":
                return JsonValue.Create(Number(schema, random, integer: false));
            case "boolean":
                return JsonValue.Create(random.Next(2) == 0);
            case "string":
                return JsonValue.Create(Text(schema, random));
            default:
                return suite.Samples.TrySample(schemaNode, false, out var sample) ? sample : null;
        }
    }

    private static decimal Number(JsonObject schema, Random random, bool integer)
    {
        var low = SchemaValidator.Number(schema, "minimum") ?? SchemaValidator.ExclusiveBound(schema, "exclusiveMinimum", "minimum") + (integer ? 1 : 0.01m) ?? -1000;
        var high = SchemaValidator.Number(schema, "maximum") ?? SchemaValidator.ExclusiveBound(schema, "exclusiveMaximum", "maximum") - (integer ? 1 : 0.01m) ?? low + 2000;
        var value = low + (high - low) * (decimal)random.NextDouble();
        if (SchemaValidator.Number(schema, "multipleOf") is { } step and > 0)
        {
            value = Math.Ceiling(value / step) * step;
        }

        return integer ? Math.Round(value, MidpointRounding.ToZero) : Math.Round(value, 2);
    }

    private string Text(JsonObject schema, Random random)
    {
        switch (schema["format"]?.GetValue<string>())
        {
            case "date-time":
                return new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(random.Next(0, 5_000_000)).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            case "date":
                return new DateOnly(2020, 1, 1).AddDays(random.Next(0, 3650)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            case "uuid":
                var bytes = new byte[16];
                random.NextBytes(bytes);
                return new Guid(bytes).ToString();
            case "email":
                return $"user{random.Next(10_000)}@example.com";
        }

        if (schema["pattern"] is not null)
        {
            // A pattern can't be drawn from at random; its valid sample is used.
            return suite.Samples.TrySample(schema, false, out var sample) && sample is JsonValue v ? v.GetValue<string>() : "sample";
        }

        const string Alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 -_.é";
        var min = (int)(SchemaValidator.Number(schema, "minLength") ?? 0);
        var max = (int)Math.Min(SchemaValidator.Number(schema, "maxLength") ?? min + 12, min + 12);
        var text = new StringBuilder();
        for (var i = random.Next(min, max + 1); i > 0; i--)
        {
            text.Append(Alphabet[random.Next(Alphabet.Length)]);
        }

        return text.ToString();
    }

    /// <summary>The smallest request, by greedy steps, that still fails a check the original failed.</summary>
    private async Task<CaseResult> ShrinkAsync(CaseResult failing, CancellationToken cancellationToken)
    {
        var checks = failing.Findings.Where(f => f.Severity == Severity.Failure).Select(f => f.Check).ToHashSet(StringComparer.Ordinal);
        var best = failing;
        var steps = 0;
        var runs = 0;
        var improved = true;
        while (improved && runs < ShrinkLimit)
        {
            improved = false;
            foreach (var candidate in Smaller(best.Case))
            {
                if (++runs > ShrinkLimit)
                {
                    break;
                }

                var result = await RunCaseAsync(candidate, cancellationToken).ConfigureAwait(false);
                if (result.Findings.Any(f => f.Severity == Severity.Failure && checks.Contains(f.Check)))
                {
                    best = result;
                    steps++;
                    improved = true;
                    break;
                }
            }
        }

        return steps == 0 ? best : best with { Case = best.Case with { Name = $"{failing.Case.Name}, shrunk in {steps} steps" } };
    }

    /// <summary>Each request one step smaller than this one, and still valid.</summary>
    private IEnumerable<RegressionCase> Smaller(RegressionCase @case)
    {
        var request = @case.Request;
        foreach (var parameter in request.Query.Where(q => @case.Operation.Parameters.Any(p => p.Name == q.Key && p.In == "query" && !p.Required)))
        {
            yield return @case with { Request = request with { Query = request.Query.Where(q => !q.Equals(parameter)).ToList() } };
        }

        if (request.Body is null || @case.Operation.Body is not { } schema || JsonNode.Parse(request.Body) is not { } body)
        {
            yield break;
        }

        foreach (var smaller in Smaller(body))
        {
            if (suite.Validator.IsValid(smaller, schema.Schema, Direction.Request) && !SchemaRules.Broken(suite.Spec, smaller, schema.Schema).Any(b => b.Error is null))
            {
                yield return @case with { Request = request with { Body = smaller.ToJsonString() } };
            }
        }
    }

    /// <summary>A value one step smaller: a property left out, a string or array shorter, a number nearer zero.</summary>
    private static IEnumerable<JsonNode> Smaller(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var name in obj.Select(p => p.Key).ToList())
                {
                    var without = (JsonObject)obj.DeepClone();
                    without.Remove(name);
                    yield return without;
                }

                foreach (var (name, child) in obj.ToList())
                {
                    foreach (var smaller in child is null ? [] : Smaller(child))
                    {
                        var copy = (JsonObject)obj.DeepClone();
                        copy[name] = smaller;
                        yield return copy;
                    }
                }

                break;
            case JsonArray array when array.Count > 0:
                yield return new JsonArray(array.Skip(1).Select(i => i?.DeepClone()).ToArray());
                break;
            case JsonValue value when value.GetValueKind() == JsonValueKind.String && value.GetValue<string>() is { Length: > 0 } text:
                yield return JsonValue.Create(text[..(text.Length / 2)]);
                yield return JsonValue.Create(text[..^1]);
                break;
            case JsonValue value when SchemaValidator.NumberOf(value) is { } number && number != 0:
                yield return JsonValue.Create(0);
                yield return JsonValue.Create(Math.Truncate(number / 2));
                yield return JsonValue.Create(number > 0 ? number - 1 : number + 1);
                break;
        }
    }
}
