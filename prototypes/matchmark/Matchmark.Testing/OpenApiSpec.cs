using System.Text.Json.Nodes;
using Matchmark.Rules;

namespace Matchmark.Testing;

/// <summary>An OpenAPI 3.x document (JSON), read as far as the regression engine needs it.</summary>
public sealed class OpenApiSpec
{
    private readonly JsonNode _document;

    private OpenApiSpec(JsonNode document)
    {
        _document = document;
        Operations = ReadOperations().ToList();
    }

    public IReadOnlyList<SpecOperation> Operations { get; }

    public static OpenApiSpec Parse(string json)
        => new(JsonNode.Parse(json) ?? throw new ArgumentException("The spec is empty.", nameof(json)));

    public SpecOperation Operation(string id)
        => Operations.FirstOrDefault(o => o.Id == id) ?? throw new KeyNotFoundException($"The spec has no operation '{id}'.");

    /// <summary>The schema a $ref points at, or the schema itself.</summary>
    public JsonObject Resolve(JsonNode schema)
    {
        var seen = 0;
        while (schema is JsonObject o && o["$ref"]?.GetValue<string>() is { } reference)
        {
            schema = Find(reference) ?? throw new InvalidOperationException($"The spec has no '{reference}'.");
            if (++seen > 32)
            {
                throw new InvalidOperationException($"'{reference}' refers to itself.");
            }
        }

        return schema as JsonObject ?? new JsonObject();
    }

    /// <summary>The component schema name of a $ref, if <paramref name="schema"/> is one.</summary>
    public static string? RefName(JsonNode? schema)
        => schema?["$ref"]?.GetValue<string>() is { } reference ? reference[(reference.LastIndexOf('/') + 1)..] : null;

    public JsonNode? Find(string reference)
    {
        if (!reference.StartsWith("#/", StringComparison.Ordinal))
        {
            return null;
        }

        JsonNode? node = _document;
        foreach (var segment in reference[2..].Split('/'))
        {
            node = node?[segment.Replace("~1", "/").Replace("~0", "~")];
        }

        return node;
    }

    private IEnumerable<SpecOperation> ReadOperations()
    {
        foreach (var (path, item) in _document["paths"]?.AsObject() ?? [])
        {
            var shared = Parameters(item?["parameters"]);

            // OpenAPI 3.2 adds query (the QUERY method, RFC 10008), and additionalOperations for any other method.
            var additional = (item?["additionalOperations"] as JsonObject ?? []).Select(o => (Method: o.Key.ToLowerInvariant(), Node: o.Value));
            foreach (var (method, node) in (item?.AsObject() ?? []).Select(o => (Method: o.Key, Node: o.Value)).Concat(additional))
            {
                if (method is "parameters" or "summary" or "description" or "servers" or "$ref" or "additionalOperations" || method.StartsWith("x-", StringComparison.Ordinal)
                    || node is not JsonObject operation || operation["responses"] is null)
                {
                    continue;
                }

                var own = Parameters(operation["parameters"]);
                var parameters = own.Concat(shared.Where(s => own.All(o => o.Name != s.Name || o.In != s.In))).ToList();
                var id = operation["operationId"]?.GetValue<string>() ?? $"{method.ToUpperInvariant()} {path}";
                yield return new SpecOperation(id, method.ToUpperInvariant(), path, parameters, Body(operation["requestBody"]), Responses(operation["responses"]))
                {
                    Deprecated = operation["deprecated"]?.GetValueKind() == System.Text.Json.JsonValueKind.True,
                    Json = operation,
                };
            }
        }
    }

    private List<SpecParameter> Parameters(JsonNode? node)
        => (node as JsonArray ?? []).Select(p => Resolve(p!)).Select(p => new SpecParameter(
                p["name"]!.GetValue<string>(),
                p["in"]!.GetValue<string>(),
                p["required"]?.GetValue<bool>() == true || p["in"]!.GetValue<string>() == "path",
                p["schema"] ?? new JsonObject(),
                p["example"] ?? FirstExample(p["examples"])))
            .ToList();

    private SpecBody? Body(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        var body = Resolve(node);
        var (mediaType, content) = (body["content"]?.AsObject() ?? []).FirstOrDefault(c => IsJson(c.Key));
        return content is null
            ? null
            : new SpecBody(body["required"]?.GetValue<bool>() == true, mediaType, content["schema"] ?? new JsonObject(),
                content["example"] ?? FirstExample(content["examples"]));
    }

    private Dictionary<string, SpecResponse> Responses(JsonNode? node)
    {
        var responses = new Dictionary<string, SpecResponse>(StringComparer.Ordinal);
        foreach (var (status, value) in node?.AsObject() ?? [])
        {
            var response = Resolve(value!);
            var content = (response["content"]?.AsObject() ?? [])
                .ToDictionary(c => c.Key, c => c.Value?["schema"], StringComparer.OrdinalIgnoreCase);
            var headers = (response["headers"]?.AsObject() ?? [])
                .Select(h => (Name: h.Key, Header: Resolve(h.Value!)))
                .Select(h => new SpecHeader(h.Name, h.Header["required"]?.GetValue<bool>() == true, h.Header["schema"] ?? new JsonObject()))
                .ToList();
            var links = (response["links"]?.AsObject() ?? [])
                .Select(l => (Name: l.Key, Link: Resolve(l.Value!)))
                .Where(l => l.Link["operationId"] is not null)
                .Select(l => new SpecLink(
                    l.Name,
                    l.Link["operationId"]!.GetValue<string>(),
                    (l.Link["parameters"]?.AsObject() ?? []).ToDictionary(p => p.Key, p => p.Value!.ToJsonString().Trim('"'), StringComparer.Ordinal),
                    l.Link["requestBody"]))
                .ToList();
            var items = (response["content"]?.AsObject() ?? []).Where(c => c.Value?["itemSchema"] is not null)
                .ToDictionary(c => c.Key, c => c.Value!["itemSchema"]!, StringComparer.OrdinalIgnoreCase);
            responses[status] = new SpecResponse(status, content, headers, links) { ItemSchemas = items };
        }

        return responses;
    }

    private static JsonNode? FirstExample(JsonNode? examples)
        => examples is JsonObject named ? named.Select(e => e.Value?["value"]).FirstOrDefault(v => v is not null) : null;

    /// <summary>application/json, application/problem+json, anything +json.</summary>
    public static bool IsJson(string mediaType)
    {
        var type = mediaType.Split(';')[0].Trim();
        return type.Equals("application/json", StringComparison.OrdinalIgnoreCase)
               || type.EndsWith("+json", StringComparison.OrdinalIgnoreCase)
               || type == "*/*";
    }
}

public sealed record SpecOperation(
    string Id, string Method, string Path, IReadOnlyList<SpecParameter> Parameters, SpecBody? Body, IReadOnlyDictionary<string, SpecResponse> Responses)
{
    /// <summary>The operation as written, for its extensions (<c>x-dependencies</c>, <c>x-matchmark-ensures</c>...).</summary>
    public JsonObject Json { get; init; } = [];

    /// <summary>Its <c>x-dependencies</c>, parsed. One that can't be parsed is left out (and reported as a gap by the suite).</summary>
    public IReadOnlyList<Idl> Dependencies => (Json["x-dependencies"] as JsonArray ?? []).Select(d => d?.GetValue<string>())
        .Where(d => d is not null).Select(d => { try { return Idl.Parse(d!); } catch (FormatException) { return null; } })
        .Where(d => d is not null).Select(d => d!).ToList();

    /// <summary>Marked <c>deprecated: true</c>: its responses must announce it (RFC 9745).</summary>
    public bool Deprecated { get; init; }

    /// <summary>Takes an <c>Idempotency-Key</c> header (draft-ietf-httpapi-idempotency-key-header): retries with the same key are safe.</summary>
    public bool Idempotent => Parameters.Any(p => p.In == "header" && string.Equals(p.Name, "Idempotency-Key", StringComparison.OrdinalIgnoreCase));

    /// <summary>Takes an <c>If-Match</c> header: a change against a stale ETag must be refused (RFC 9110 §13.1.1).</summary>
    public bool Conditional => Parameters.Any(p => p.In == "header" && string.Equals(p.Name, "If-Match", StringComparison.OrdinalIgnoreCase));

    /// <summary>The documented response for a status: exact, then its range (2XX), then default.</summary>
    public SpecResponse? Response(int status)
        => Responses.GetValueOrDefault(status.ToString(System.Globalization.CultureInfo.InvariantCulture))
           ?? Responses.GetValueOrDefault($"{status / 100}XX")
           ?? Responses.GetValueOrDefault("default");

    public override string ToString() => $"{Id} ({Method} {Path})";
}

/// <summary>A path, query or header parameter. Path parameters are always required.</summary>
public sealed record SpecParameter(string Name, string In, bool Required, JsonNode Schema, JsonNode? Example);

public sealed record SpecBody(bool Required, string MediaType, JsonNode Schema, JsonNode? Example);

public sealed record SpecResponse(
    string Status, IReadOnlyDictionary<string, JsonNode?> Content, IReadOnlyList<SpecHeader> Headers, IReadOnlyList<SpecLink> Links)
{
    /// <summary>OpenAPI 3.2's itemSchema, by media type: the schema of each item of a sequential body (JSON Lines, server-sent events).</summary>
    public IReadOnlyDictionary<string, JsonNode> ItemSchemas { get; init; } = new Dictionary<string, JsonNode>();
}

public sealed record SpecHeader(string Name, bool Required, JsonNode Schema);

/// <summary>An OpenAPI link: which operation a response leads to, and how its parameters come from this exchange.</summary>
public sealed record SpecLink(string Name, string OperationId, IReadOnlyDictionary<string, string> Parameters, JsonNode? RequestBody);
