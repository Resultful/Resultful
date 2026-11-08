using System.Text.Json.Nodes;

namespace Matchmark.Testing;

/// <summary>
/// An AsyncAPI 3 document (JSON), read as far as the engine needs it: its operations, and the messages each sends or
/// receives. A consumer (the <c>receive</c> operations) is tested with the regression suite over an HTTP bridge: each
/// operation is <c>POST /{operationId}</c> of the bridge's base address, its message's payload the body and its
/// headers the request's headers; the bridge hands the message to the consumer and answers 2xx when it's accepted, 400
/// when it's refused. A producer (the <c>send</c> operations) is tested by checking each message it sends
/// (<see cref="Check"/>). The broker itself (Kafka, AMQP, MQTT) is the bridge's business, not the engine's.
/// </summary>
public sealed class AsyncApiDocument
{
    private readonly JsonObject _document;
    private readonly SchemaValidator _validator;

    private AsyncApiDocument(JsonObject document)
    {
        _document = document;
        _validator = new SchemaValidator(OpenApiSpec.Parse(document.ToJsonString()));
        Operations = ReadOperations().ToList();
    }

    public IReadOnlyList<AsyncApiOperation> Operations { get; }

    public static AsyncApiDocument Parse(string json)
    {
        var document = JsonNode.Parse(json) as JsonObject ?? throw new ArgumentException("The document isn't a JSON object.", nameof(json));
        return document["asyncapi"]?.GetValue<string>() is { } version && version.StartsWith("3.", StringComparison.Ordinal)
            ? new AsyncApiDocument(document)
            : throw new ArgumentException($"Only AsyncAPI 3 is read; this document is '{document["asyncapi"]}'.", nameof(json));
    }

    /// <summary>
    /// The suite for the document's <c>receive</c> operations, to run against a bridge to the consumer (see the class
    /// summary). Null when the document receives nothing.
    /// </summary>
    public RegressionSuite? ConsumerSuite()
    {
        var paths = new JsonObject();
        foreach (var operation in Operations.Where(o => o.Action == "receive" && o.Messages.Count > 0))
        {
            paths["/" + operation.Id] = new JsonObject
            {
                ["post"] = new JsonObject
                {
                    ["operationId"] = operation.Id,
                    ["summary"] = $"Receive on {operation.Channel}",
                    ["parameters"] = Headers(operation),
                    ["requestBody"] = new JsonObject
                    {
                        ["required"] = true,
                        ["content"] = new JsonObject { [operation.Messages[0].ContentType] = new JsonObject { ["schema"] = Payload(operation) } },
                    },
                    ["responses"] = new JsonObject
                    {
                        ["202"] = new JsonObject { ["description"] = "The consumer accepted the message." },
                        ["400"] = new JsonObject { ["description"] = "The consumer refused the message." },
                    },
                },
            };
        }

        return paths.Count == 0
            ? null
            : RegressionSuite.FromJson(new JsonObject
            {
                ["openapi"] = "3.1.0",
                ["info"] = _document["info"]?.DeepClone(),
                ["paths"] = paths,
                ["components"] = _document["components"]?.DeepClone(),
            }.ToJsonString());
    }

    /// <summary>
    /// What's wrong with a message sent by <paramref name="operationId"/>: empty when the payload (and the headers, if
    /// given) conform to one of the operation's messages.
    /// </summary>
    public IReadOnlyList<string> Check(string operationId, JsonNode? payload, JsonObject? headers = null)
    {
        var operation = Operations.FirstOrDefault(o => o.Id == operationId) ?? throw new KeyNotFoundException($"The document has no operation '{operationId}'.");
        var errors = new List<string>();
        foreach (var message in operation.Messages)
        {
            var found = _validator.Validate(payload, message.Payload, "payload");
            if (headers is not null && message.Headers is { } schema)
            {
                found.AddRange(_validator.Validate(headers, schema, "headers"));
            }

            if (found.Count == 0)
            {
                return [];
            }

            errors.AddRange(operation.Messages.Count == 1 ? found : found.Select(e => $"{message.Name}: {e}"));
        }

        return errors;
    }

    private IEnumerable<AsyncApiOperation> ReadOperations()
    {
        var defaultContentType = _document["defaultContentType"]?.GetValue<string>() ?? "application/json";
        foreach (var (id, node) in _document["operations"] as JsonObject ?? [])
        {
            if (Resolve(node) is not { } operation)
            {
                continue;
            }

            var channel = Resolve(operation["channel"]);
            // An operation's messages are a subset of its channel's; none listed means all of them.
            var references = operation["messages"] is JsonArray listed ? listed.Select(Resolve) : (channel?["messages"] as JsonObject ?? []).Select(m => Resolve(m.Value));
            var messages = new List<AsyncApiMessage>();
            foreach (var message in references.OfType<JsonObject>())
            {
                if (Schema(message["payload"]) is { } payload)
                {
                    messages.Add(new AsyncApiMessage(
                        message["name"]?.GetValue<string>() ?? message["title"]?.GetValue<string>() ?? $"message {messages.Count + 1}",
                        message["contentType"]?.GetValue<string>() ?? defaultContentType,
                        payload,
                        Schema(message["headers"])));
                }
            }

            yield return new AsyncApiOperation(id, operation["action"]?.GetValue<string>() ?? "send", channel?["address"]?.GetValue<string>() ?? id, messages);
        }
    }

    /// <summary>A message's headers, as header parameters: each property of its headers schema.</summary>
    private static JsonArray Headers(AsyncApiOperation operation)
    {
        var parameters = new JsonArray();
        if (operation.Messages.Count == 1 && operation.Messages[0].Headers is JsonObject { } headers && headers["properties"] is JsonObject properties)
        {
            var required = (headers["required"] as JsonArray ?? []).Select(r => r?.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
            foreach (var (name, schema) in properties)
            {
                parameters.Add(new JsonObject { ["name"] = name, ["in"] = "header", ["required"] = required.Contains(name), ["schema"] = schema?.DeepClone() });
            }
        }

        return parameters;
    }

    /// <summary>The body's schema: the message's payload, or one of the messages' when there are several.</summary>
    private static JsonNode Payload(AsyncApiOperation operation)
        => operation.Messages.Count == 1
            ? operation.Messages[0].Payload.DeepClone()
            : new JsonObject { ["oneOf"] = new JsonArray(operation.Messages.Select(m => m.Payload.DeepClone()).ToArray()) };

    /// <summary>A schema, out of a multi-format schema object when it's one; null when it's in a format other than JSON Schema.</summary>
    private static JsonNode? Schema(JsonNode? node)
    {
        if (node is JsonObject { } multi && multi["schemaFormat"]?.GetValue<string>() is { } format && multi["schema"] is { } schema)
        {
            return format.StartsWith("application/vnd.aai.asyncapi", StringComparison.Ordinal) || format.StartsWith("application/schema+json", StringComparison.Ordinal)
                ? schema
                : null;
        }

        return node;
    }

    /// <summary>A $ref within the document followed to what it names (channels, operations and messages all use them).</summary>
    private JsonObject? Resolve(JsonNode? node)
    {
        for (var seen = 0; node is JsonObject o && o["$ref"]?.GetValue<string>() is { } reference && seen < 32; seen++)
        {
            node = reference.StartsWith("#/", StringComparison.Ordinal)
                ? reference[2..].Split('/').Aggregate((JsonNode?)_document, (n, s) => n?[s.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal)])
                : null;
        }

        return node as JsonObject;
    }
}

/// <summary>An AsyncAPI operation: the application sends or receives these messages on this channel.</summary>
public sealed record AsyncApiOperation(string Id, string Action, string Channel, IReadOnlyList<AsyncApiMessage> Messages);

/// <summary>A message: its payload's schema, and its headers' if it has any.</summary>
public sealed record AsyncApiMessage(string Name, string ContentType, JsonNode Payload, JsonNode? Headers);
