using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Matchmark.Generator
{
    /// <summary>
    /// An AsyncAPI 3 document read as OpenAPI, so that everything generated for webhooks is generated for its messages:
    /// each operation becomes a webhook named by its operationId, its message's payload the body and its headers header
    /// parameters, answered 202 when taken and 400 when refused. The payloads become typed models; a <c>receive</c>
    /// operation gets the receiver interface the consumer implements, and a <c>send</c> operation the sender the producer
    /// calls (both are generated for each, as for any webhook). The broker sits behind a bridge that maps a message to
    /// <c>POST /{operationId}</c> and back.
    /// </summary>
    public static class AsyncApi
    {
        private static readonly Regex Version = new Regex(@"^\s*[""']?asyncapi[""']?\s*:\s*[""']?3\.", RegexOptions.Multiline);

        public static bool IsAsyncApi(string text) => Version.IsMatch(text);

        /// <summary>The document (YAML or JSON) as OpenAPI 3.1 JSON.</summary>
        public static string ToOpenApi(string text, string path)
        {
            var document = Overlay.Parse(text, path) as JsonObject ?? throw new InvalidOperationException($"{path} isn't an object.");
            var defaultContentType = document["defaultContentType"]?.GetValue<string>() ?? "application/json";
            var webhooks = new JsonObject();
            foreach (var pair in document["operations"] as JsonObject ?? new JsonObject())
            {
                if (!(Resolve(document, pair.Value) is JsonObject operation))
                {
                    continue;
                }

                var channel = Resolve(document, operation["channel"]);
                var references = operation["messages"] is JsonArray listed
                    ? listed.Select(m => Resolve(document, m))
                    : (channel?["messages"] as JsonObject ?? new JsonObject()).Select(m => Resolve(document, m.Value));
                var messages = references.OfType<JsonObject>().Where(m => m["payload"] != null).ToList();
                if (messages.Count == 0)
                {
                    continue;
                }

                var payloads = messages.Select(m => Schema(m["payload"])).ToList();
                var action = operation["action"]?.GetValue<string>() ?? "send";
                var address = channel?["address"]?.GetValue<string>() ?? pair.Key;
                webhooks[pair.Key] = new JsonObject
                {
                    ["post"] = new JsonObject
                    {
                        ["operationId"] = pair.Key,
                        ["summary"] = operation["summary"]?.DeepClone() ?? JsonValue.Create($"{(action == "receive" ? "Receives" : "Sends")} on {address}"),
                        ["x-asyncapi-action"] = action,
                        ["x-asyncapi-channel"] = address,
                        ["parameters"] = messages.Count == 1 ? Headers(Resolve(document, messages[0]["headers"])) : new JsonArray(),
                        ["requestBody"] = new JsonObject
                        {
                            ["required"] = true,
                            ["content"] = new JsonObject
                            {
                                [messages[0]["contentType"]?.GetValue<string>() ?? defaultContentType] = new JsonObject
                                {
                                    ["schema"] = payloads.Count == 1 ? payloads[0] : new JsonObject { ["oneOf"] = new JsonArray(payloads.ToArray()) },
                                },
                            },
                        },
                        ["responses"] = new JsonObject
                        {
                            ["202"] = new JsonObject { ["description"] = "The message was taken." },
                            ["415"] = new JsonObject { ["description"] = "The message isn't in its content type." },
                            ["400"] = new JsonObject
                            {
                                ["description"] = "The message breaks its schema.",
                                ["content"] = new JsonObject
                                {
                                    ["application/problem+json"] = new JsonObject { ["schema"] = new JsonObject { ["$ref"] = "#/components/schemas/HttpValidationProblemDetails" } },
                                },
                            },
                        },
                    },
                };
            }

            var schemas = document["components"]?["schemas"]?.DeepClone() as JsonObject ?? new JsonObject();
            if (!schemas.ContainsKey("HttpValidationProblemDetails"))
            {
                schemas["HttpValidationProblemDetails"] = JsonNode.Parse(
                    @"{""type"":""object"",""properties"":{""type"":{""type"":[""string"",""null""]},""title"":{""type"":[""string"",""null""]},""status"":{""type"":[""integer"",""null""],""format"":""int32""},""detail"":{""type"":[""string"",""null""]},""instance"":{""type"":[""string"",""null""]},""errors"":{""type"":""object"",""additionalProperties"":{""type"":""array"",""items"":{""type"":""string""}}}}}");
            }

            return new JsonObject
            {
                ["openapi"] = "3.1.0",
                ["info"] = document["info"]?.DeepClone() ?? new JsonObject { ["title"] = "Events", ["version"] = "1.0.0" },
                ["paths"] = new JsonObject(),
                ["webhooks"] = webhooks,
                ["components"] = new JsonObject { ["schemas"] = schemas },
            }.ToJsonString();
        }

        /// <summary>A message's headers schema, as header parameters.</summary>
        private static JsonArray Headers(JsonObject? headers)
        {
            var parameters = new JsonArray();
            if (headers?["properties"] is JsonObject properties)
            {
                var required = new HashSet<string>((headers["required"] as JsonArray ?? new JsonArray()).Select(r => r?.GetValue<string>() ?? string.Empty), StringComparer.Ordinal);
                foreach (var property in properties)
                {
                    parameters.Add(new JsonObject { ["name"] = property.Key, ["in"] = "header", ["required"] = required.Contains(property.Key), ["schema"] = property.Value?.DeepClone() });
                }
            }

            return parameters;
        }

        /// <summary>A payload's schema, out of a multi-format schema object when it's one.</summary>
        private static JsonNode Schema(JsonNode? payload)
            => (payload is JsonObject multi && multi["schemaFormat"] != null && multi["schema"] is JsonNode schema ? schema : payload)?.DeepClone() ?? new JsonObject();

        private static JsonObject? Resolve(JsonObject document, JsonNode? node)
        {
            for (var seen = 0; node is JsonObject o && o["$ref"]?.GetValue<string>() is string reference && seen < 32; seen++)
            {
                node = reference.StartsWith("#/", StringComparison.Ordinal)
                    ? reference.Substring(2).Split('/').Aggregate((JsonNode?)document, (n, s) => n?[s.Replace("~1", "/").Replace("~0", "~")])
                    : null;
            }

            return node as JsonObject;
        }
    }
}
