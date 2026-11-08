using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Matchmark.Testing;

/// <summary>Sequential bodies (OpenAPI 3.2's itemSchema): each item checked against the item schema, as it would arrive.</summary>
internal sealed partial class RegressionRunner
{
    /// <summary>
    /// JSON Lines (<c>application/jsonl</c>, <c>application/x-ndjson</c>), JSON text sequences (<c>application/json-seq</c>)
    /// and server-sent events (<c>text/event-stream</c>, each event as the object 3.2 describes: <c>data</c>, <c>event</c>,
    /// <c>id</c>, <c>retry</c>). An item that isn't JSON, or breaks the schema, is a BodyConforms failure naming the item.
    /// </summary>
    private void Items(Exchange exchange, string mediaType, JsonNode itemSchema, string subject, List<Finding> findings)
    {
        var index = 0;
        foreach (var (item, error) in Split(exchange.Body, mediaType))
        {
            index++;
            if (error is not null)
            {
                Add(findings, Checks.BodyConforms, subject, exchange, $"item {index} of the {mediaType} stream {error}");
                continue;
            }

            foreach (var problem in suite.Validator.Validate(item, itemSchema, direction: Direction.Response).Take(5))
            {
                Add(findings, Checks.BodyConforms, subject, exchange, $"item {index} of the {mediaType} stream breaks its itemSchema: {problem}");
            }

            // A server-sent event's data, as its contentSchema says (JSON Schema's content vocabulary).
            if (item?["data"] is JsonValue data && data.GetValueKind() == JsonValueKind.String
                && suite.Spec.Resolve(itemSchema)["properties"]?["data"] is JsonObject dataSchema && dataSchema["contentSchema"] is { } contentSchema)
            {
                JsonNode? content = null;
                try
                {
                    content = JsonNode.Parse(data.GetValue<string>());
                }
                catch (JsonException)
                {
                    Add(findings, Checks.BodyConforms, subject, exchange, $"event {index}'s data isn't JSON: {RegressionReport.Excerpt(data.GetValue<string>())}");
                    continue;
                }

                foreach (var problem in suite.Validator.Validate(content, contentSchema, direction: Direction.Response).Take(5))
                {
                    Add(findings, Checks.BodyConforms, subject, exchange, $"event {index}'s data breaks its contentSchema: {problem}");
                }
            }
        }
    }

    internal static IEnumerable<(JsonNode? Item, string? Error)> Split(string body, string mediaType)
    {
        var type = mediaType.Split(';')[0].Trim().ToLowerInvariant();
        if (type == "text/event-stream")
        {
            foreach (var block in body.Replace("\r\n", "\n", StringComparison.Ordinal).Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = new JsonObject();
                var data = new StringBuilder();
                foreach (var line in block.Split('\n').Where(l => l.Length > 0 && l[0] != ':'))
                {
                    var colon = line.IndexOf(':', StringComparison.Ordinal);
                    var (name, value) = colon < 0 ? (line, string.Empty) : (line[..colon], line[(colon + 1)..].TrimStart(' '));
                    switch (name)
                    {
                        case "data":
                            data.Append(data.Length > 0 ? "\n" : string.Empty).Append(value);
                            break;
                        case "retry" when int.TryParse(value, out var retry):
                            fields["retry"] = retry;
                            break;
                        case "event" or "id":
                            fields[name] = value;
                            break;
                    }
                }

                if (data.Length > 0 || fields.Count > 0)
                {
                    fields["data"] = data.ToString();
                    yield return (fields, null);
                }
            }

            yield break;
        }

        var separators = type == "application/json-seq" ? new[] { '\u001e', '\n' } : ['\n'];
        foreach (var text in body.Split(separators, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).Where(t => t.Length > 0))
        {
            JsonNode? item = null;
            string? error = null;
            try
            {
                item = JsonNode.Parse(text);
            }
            catch (JsonException)
            {
                error = $"isn't JSON: {RegressionReport.Excerpt(text)}";
            }

            yield return (item, error);
        }
    }
}
