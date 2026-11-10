using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Matchmark.Testing;

/// <summary>A call seen on the wire: what was asked, and what came back.</summary>
public sealed record RecordedCall(string Method, string Path, IReadOnlyList<KeyValuePair<string, string>> Query, string? RequestType, string? RequestBody,
    int Status, string? ResponseType, string? ResponseBody);

/// <summary>
/// Step 1 of the flip for an application that isn't ASP.NET Core (decision Q14, docs/reference/flip.md): a starting spec
/// inferred from recorded traffic. Paths are templated where a segment is an id, schemas are inferred from every body
/// seen (a field every sample has is required), and each status seen is an outcome. It's what the traffic showed, not
/// what the API promises: review it before verifying the application against it.
/// </summary>
public static partial class TrafficDerive
{
    /// <summary>The calls in a HAR file (HTTP Archive 1.2, as browsers and proxies export it).</summary>
    public static IReadOnlyList<RecordedCall> Har(string json)
    {
        var entries = JsonNode.Parse(json)?["log"]?["entries"] as JsonArray ?? throw new InvalidOperationException("Not a HAR file: it has no log.entries.");
        var calls = new List<RecordedCall>();
        foreach (var entry in entries.OfType<JsonObject>())
        {
            if (entry["request"] is not JsonObject request || entry["response"] is not JsonObject response
                || !Uri.TryCreate(request["url"]?.GetValue<string>(), UriKind.Absolute, out var url))
            {
                continue;
            }

            calls.Add(new RecordedCall(
                request["method"]?.GetValue<string>()?.ToUpperInvariant() ?? "GET",
                url.AbsolutePath,
                SpecTwin.Query(url.Query),
                request["postData"]?["mimeType"]?.GetValue<string>()?.Split(';')[0].Trim(),
                request["postData"]?["text"]?.GetValue<string>(),
                response["status"]?.GetValue<int>() ?? 0,
                response["content"]?["mimeType"]?.GetValue<string>()?.Split(';')[0].Trim(),
                response["content"]?["text"]?.GetValue<string>()));
        }

        return calls;
    }

    /// <summary>The spec the traffic shows, titled <paramref name="title"/>, and what to review in it.</summary>
    public static (JsonObject Spec, IReadOnlyList<string> Review) Derive(IReadOnlyList<RecordedCall> calls, string title = "Derived from traffic")
    {
        var review = new List<string>();
        var paths = new JsonObject();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        // Templates are learned from id-shaped segments, then applied to any value in that place (/pets/not-a-number is
        // still /pets/{petId}, refused), so a refused request doesn't become an operation of its own.
        var learned = calls.Select(c => Template(c.Path)).Where(t => t.Parameters.Count > 0).Select(t => t.Template).Distinct(StringComparer.Ordinal).ToList();
        string Templated(string path)
        {
            var own = Template(path).Template;
            var segments = own.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return learned.FirstOrDefault(t => t.Split('/', StringSplitOptions.RemoveEmptyEntries) is var parts && parts.Length == segments.Length
                && parts.Zip(segments).All(p => p.First == p.Second || p.First.StartsWith('{'))) ?? own;
        }

        var guessedEnums = new List<string>();
        foreach (var group in calls.Where(c => c.Status > 0).GroupBy(c => (c.Method, Template: Templated(c.Path)))
                     .OrderBy(g => g.Key.Template, StringComparer.Ordinal).ThenBy(g => g.Key.Method, StringComparer.Ordinal))
        {
            var (method, template) = group.Key;
            var parameters = template.Split('/', StringSplitOptions.RemoveEmptyEntries).Select((s, i) => (Segment: s, Index: i))
                .Where(s => s.Segment.StartsWith('{')).Select(s => (s.Segment.Trim('{', '}'), s.Index)).ToList();
            var operation = new JsonObject();
            var id = SpecDerive.OperationId(method, template);
            for (var n = 2; !ids.Add(id); n++)
            {
                id = SpecDerive.OperationId(method, template) + n.ToString(CultureInfo.InvariantCulture);
            }

            operation["operationId"] = id;
            var list = group.ToList();
            var parameterList = new JsonArray();
            foreach (var (name, index) in parameters)
            {
                // The path values of calls that succeeded: a refused one shows what isn't an id.
                var values = list.Where(c => c.Status is >= 200 and < 400).Select(c => c.Path.Split('/', StringSplitOptions.RemoveEmptyEntries)).Where(s => s.Length > index).Select(s => s[index]).ToList();
                parameterList.Add(new JsonObject { ["name"] = name, ["in"] = "path", ["required"] = true, ["schema"] = Scalar(values) });
            }

            foreach (var name in list.SelectMany(c => c.Query.Select(q => q.Key)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                var values = list.Where(c => c.Status is >= 200 and < 400).SelectMany(c => c.Query.Where(q => q.Key == name).Select(q => q.Value)).ToList();
                parameterList.Add(new JsonObject
                {
                    ["name"] = name, ["in"] = "query", ["required"] = list.All(c => c.Query.Any(q => q.Key == name)), ["schema"] = Scalar(values),
                });
            }

            if (parameterList.Count > 0)
            {
                operation["parameters"] = parameterList;
            }

            // The request body, from calls that succeeded: a refused one shows what the API doesn't accept.
            var accepted = list.Where(c => c.Status is >= 200 and < 400 && c.RequestBody is { Length: > 0 }).ToList();
            if (accepted.Count > 0)
            {
                var type = accepted[0].RequestType ?? "application/json";
                var bodies = accepted.Select(c => Json(c.RequestBody)).Where(b => b is not null).ToList();
                operation["requestBody"] = new JsonObject
                {
                    ["required"] = list.Where(c => c.Status is >= 200 and < 400).All(c => c.RequestBody is { Length: > 0 }),
                    ["content"] = new JsonObject { [type] = new JsonObject { ["schema"] = bodies.Count > 0 ? Infer(bodies!, guessedEnums, $"{method} {template} body") : new JsonObject { ["type"] = "string" } } },
                };
            }

            var responses = new JsonObject();
            foreach (var status in list.GroupBy(c => c.Status).OrderBy(s => s.Key))
            {
                var response = new JsonObject { ["description"] = $"Seen {status.Count()} time{(status.Count() == 1 ? string.Empty : "s")}" };
                var bodies = status.Where(c => c.ResponseBody is { Length: > 0 }).ToList();
                if (bodies.Count > 0)
                {
                    var type = bodies[0].ResponseType ?? "application/json";
                    var parsed = bodies.Select(c => Json(c.ResponseBody)).Where(b => b is not null).ToList();
                    response["content"] = new JsonObject { [type] = new JsonObject { ["schema"] = parsed.Count > 0 ? Infer(parsed!) : new JsonObject { ["type"] = "string" } } };
                }

                responses[status.Key.ToString(CultureInfo.InvariantCulture)] = response;
                if (status.Count() == 1 && status.Key >= 400)
                {
                    review.Add($"{method} {template}: {status.Key} was seen once; check it's an outcome the API documents, not an accident");
                }
            }

            operation["responses"] = responses;
            if (paths[template] is not JsonObject item)
            {
                paths[template] = item = new JsonObject();
            }

            item[method.ToLowerInvariant()] = operation;
        }

        foreach (var (template, _) in paths)
        {
            if (template.Contains('{', StringComparison.Ordinal))
            {
                review.Add($"{template}: its parameters were guessed from ids in the paths seen; rename them as the API means them");
            }
        }

        review.AddRange(guessedEnums.Select(e => e.Contains(": a union by ", StringComparison.Ordinal)
            ? $"{e}: each value's samples always had fields the others never did; check the members are the API's"
            : $"{e}: only these values were seen, so it's an enum; widen it if the API takes others"));
        review.Add($"inferred from {calls.Count} calls: fields every sample had are required, and no constraints (lengths, bounds, patterns) are known; add the ones the API keeps");
        var spec = new JsonObject
        {
            ["openapi"] = "3.1.1",
            ["info"] = new JsonObject { ["title"] = title, ["version"] = "0.1.0" },
            ["paths"] = paths,
        };
        return (spec, review);
    }

    /// <summary>A path's template: a segment that's an id (a number, a UUID, a long hex string) is a parameter named for the segment before it.</summary>
    public static (string Template, IReadOnlyList<(string Name, int Index)> Parameters) Template(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var parameters = new List<(string, int)>();
        var parts = new List<string>();
        for (var i = 0; i < segments.Length; i++)
        {
            if (Id().IsMatch(segments[i]))
            {
                var owner = i > 0 ? segments[i - 1] : "item";
                var name = (owner.EndsWith("ies", StringComparison.Ordinal) ? owner[..^3] + "y" : owner.EndsWith('s') ? owner[..^1] : owner) + "Id";
                name = Regex.Replace(name, "[^A-Za-z0-9]+(.)", m => m.Groups[1].Value.ToUpperInvariant());
                while (parameters.Any(p => p.Item1 == name))
                {
                    name += "2";
                }

                parameters.Add((name, i));
                parts.Add("{" + name + "}");
            }
            else
            {
                parts.Add(segments[i]);
            }
        }

        return ("/" + string.Join('/', parts), parameters);
    }

    /// <summary>One schema that every sample conforms to.</summary>
    public static JsonObject Infer(IReadOnlyList<JsonNode?> samples) => Infer(samples, null, string.Empty);

    /// <summary>
    /// As above, guessing an enum for a request field whose few values repeat across many samples (at most 5 distinct, each
    /// seen at least 3 times on average), and naming each guess in <paramref name="enums"/>.
    /// </summary>
    private static JsonObject Infer(IReadOnlyList<JsonNode?> samples, List<string>? enums, string where)
    {
        var present = samples.Where(s => s is not null).ToList();
        var nullable = present.Count < samples.Count;
        var kinds = present.Select(Kind).Distinct().ToList();
        JsonObject schema;
        if (kinds.Count == 1 && kinds[0] == "object" && Discriminated(present.Cast<JsonObject>().ToList(), enums, where) is { } union)
        {
            schema = union;
        }
        else if (kinds.Count == 1 && kinds[0] == "object")
        {
            var objects = present.Cast<JsonObject>().ToList();
            var properties = new JsonObject();
            // A field every sample has is required, once there are two samples to say so; one that's sometimes left out may
            // be null too, since the traffic can't tell absent from null apart.
            var required = objects.Count < 2 ? []
                : objects.SelectMany(o => o.Select(p => p.Key)).Distinct(StringComparer.Ordinal).Where(name => objects.All(o => o.ContainsKey(name))).ToList();
            foreach (var name in objects.SelectMany(o => o.Select(p => p.Key)).Distinct(StringComparer.Ordinal))
            {
                var property = Infer(objects.Where(o => o.ContainsKey(name)).Select(o => o[name]).ToList(), enums, $"{where}.{name}");
                if (!objects.All(o => o.ContainsKey(name)) && property["type"] is JsonValue type && type.GetValue<string>() != "null")
                {
                    property["type"] = new JsonArray(type.GetValue<string>(), "null");
                }

                properties[name] = property;
            }

            schema = new JsonObject { ["type"] = "object", ["properties"] = properties };
            if (required.Count > 0)
            {
                schema["required"] = new JsonArray(required.Select(r => (JsonNode?)r).ToArray());
            }
        }
        else if (kinds.Count == 1 && kinds[0] == "array")
        {
            var items = present.Cast<JsonArray>().SelectMany(a => a).ToList();
            schema = new JsonObject { ["type"] = "array" };
            if (items.Count > 0)
            {
                schema["items"] = Infer(items, enums, $"{where}[]");
            }
        }
        else if (kinds.Count == 1 && kinds[0] == "string")
        {
            schema = new JsonObject { ["type"] = "string" };
            var texts = present.Select(s => s!.GetValue<string>()).ToList();
            if (FormatOf(texts) is { } format)
            {
                schema["format"] = format;
            }
            else if (enums is not null && texts.Distinct(StringComparer.Ordinal).Count() is var distinct and > 0 and <= 5 && texts.Count >= distinct * 3)
            {
                schema["enum"] = new JsonArray(texts.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(t => (JsonNode?)t).ToArray());
                enums.Add(where);
            }
        }
        else if (kinds.Count > 0 && kinds.All(k => k is "integer" or "number"))
        {
            schema = new JsonObject { ["type"] = kinds.Contains("number") ? "number" : "integer" };
        }
        else if (kinds.Count == 1)
        {
            schema = new JsonObject { ["type"] = kinds[0] };
        }
        else
        {
            // Mixed kinds, or only nulls: anything the traffic showed.
            schema = kinds.Count == 0 ? new JsonObject() : new JsonObject { ["type"] = new JsonArray(kinds.Select(k => (JsonNode?)k).ToArray()) };
        }

        if (nullable && schema["type"] is JsonValue single)
        {
            schema["type"] = new JsonArray(single.GetValue<string>(), "null");
        }

        return schema;
    }

    /// <summary>
    /// A union, when the samples are: a string field every sample has, with 2 to 5 values, each value's samples always
    /// carrying a field that another value's never do (a cat's livesLeft, a dog's goodBoy). Each value is a member, keyed
    /// by the field as a discriminator; null when the samples aren't a union.
    /// </summary>
    private static JsonObject? Discriminated(List<JsonObject> objects, List<string>? enums, string where)
    {
        if (objects.Count < 4)
        {
            return null;
        }

        foreach (var name in objects[0].Select(p => p.Key).Where(name => objects.All(o => o[name] is JsonValue v && v.GetValueKind() == JsonValueKind.String)))
        {
            var groups = objects.GroupBy(o => o[name]!.GetValue<string>(), StringComparer.Ordinal).ToList();
            if (groups.Count is < 2 or > 5 || groups.Any(g => g.Count() < 2))
            {
                continue;
            }

            HashSet<string> Always(IEnumerable<JsonObject> members) => members.Select(m => m.Select(p => p.Key).ToHashSet(StringComparer.Ordinal))
                .Aggregate((a, b) => { a.IntersectWith(b); return a; });
            HashSet<string> Ever(IEnumerable<JsonObject> members) => members.SelectMany(m => m.Select(p => p.Key)).ToHashSet(StringComparer.Ordinal);
            var distinct = groups.All(g => Always(g).Any(field => groups.Where(other => other.Key != g.Key).All(other => !Ever(other).Contains(field))));
            if (!distinct)
            {
                continue;
            }

            var members = new JsonArray();
            foreach (var group in groups.OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var member = Infer(group.Select(o => (JsonNode?)o.DeepClone()).ToList(), null, $"{where} ({name} {group.Key})");
                if (member["properties"] is JsonObject properties)
                {
                    properties[name] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(group.Key) };
                }

                members.Add(member);
            }

            enums?.Add($"{where}: a union by {name} ({string.Join(", ", groups.Select(g => g.Key).Order(StringComparer.Ordinal))})");
            return new JsonObject { ["oneOf"] = members, ["discriminator"] = new JsonObject { ["propertyName"] = name } };
        }

        return null;
    }

    private static string Kind(JsonNode? node) => node switch
    {
        JsonObject => "object",
        JsonArray => "array",
        JsonValue v => v.GetValueKind() switch
        {
            JsonValueKind.String => "string",
            JsonValueKind.True or JsonValueKind.False => "boolean",
            JsonValueKind.Number => v.ToJsonString().Contains('.', StringComparison.Ordinal) || v.ToJsonString().Contains('e', StringComparison.OrdinalIgnoreCase) ? "number" : "integer",
            _ => "null",
        },
        _ => "null",
    };

    private static string? FormatOf(List<string> texts)
        => texts.Count == 0 ? null
            : texts.All(t => Guid.TryParse(t, out _)) ? "uuid"
            : texts.All(t => DateTimeOffset.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _) && t.Contains('T', StringComparison.Ordinal)) ? "date-time"
            : texts.All(t => DateOnly.TryParseExact(t, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) ? "date"
            : null;

    private static JsonObject Scalar(List<string> values)
        => values.Count > 0 && values.All(v => long.TryParse(v, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _)) ? new JsonObject { ["type"] = "integer" }
            : values.Count > 0 && values.All(v => decimal.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) ? new JsonObject { ["type"] = "number" }
            : values.Count > 0 && values.All(v => v is "true" or "false") ? new JsonObject { ["type"] = "boolean" }
            : values.Count > 0 && values.All(v => Guid.TryParse(v, out _)) ? new JsonObject { ["type"] = "string", ["format"] = "uuid" }
            : new JsonObject { ["type"] = "string" };

    private static JsonNode? Json(string? text)
    {
        try
        {
            return text is null ? null : JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"^(\d+|[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}|[0-9a-fA-F]{16,})$")]
    private static partial Regex Id();
}
