using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace Matchmark.Generator
{
    /// <summary>
    /// AsyncAPI 2.x forward-ported to 3.1, following the AsyncAPI Initiative's guide "Migrating to v3"
    /// (asyncapi/website, markdown/docs/migration/migrating-to-v3.md), section by section. The official SDK for .NET
    /// reads both versions but doesn't convert between them, so this is Matchmark's port (decision Q25: an official
    /// migration guide makes a version viable). 3.0 is moved to 3.1, which changed nothing that breaks.
    /// Every change is listed, and anything 3.x has no place for is listed as lost.
    /// </summary>
    public static class AsyncApiUpgrade
    {
        public const string Latest = "3.1.0";

        public sealed class Result
        {
            internal Result(JsonObject document, IReadOnlyList<string> changes, IReadOnlyList<string> losses)
            {
                Document = document;
                Changes = changes;
                Losses = losses;
            }

            public JsonObject Document { get; }

            public IReadOnlyList<string> Changes { get; }

            public IReadOnlyList<string> Losses { get; }
        }

        /// <summary>The document at the latest version: 2.x converted, 3.0 moved up, 3.1 as it is.</summary>
        public static Result Upgrade(JsonObject document)
        {
            var version = document["asyncapi"]?.GetValue<string>() ?? string.Empty;
            if (version.StartsWith("3.", StringComparison.Ordinal))
            {
                var same = (JsonObject)document.DeepClone();
                var changes = new List<string>();
                if (version != Latest)
                {
                    same["asyncapi"] = Latest;
                    changes.Add($"asyncapi: {version} to {Latest} (3.1 changed nothing that breaks)");
                }

                return new Result(same, changes, new List<string>());
            }

            if (!version.StartsWith("2.", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"AsyncAPI '{version}' can't be forward-ported: only 2.x and 3.x are.");
            }

            return new Converter(document, version).Convert();
        }

        private sealed class Converter
        {
            private readonly JsonObject _source;
            private readonly string _version;
            private readonly List<string> _changes = new List<string>();
            private readonly List<string> _losses = new List<string>();
            private readonly JsonObject _securitySchemes;

            public Converter(JsonObject source, string version)
            {
                _source = source;
                _version = version;
                _securitySchemes = source["components"]?["securitySchemes"]?.DeepClone() as JsonObject ?? new JsonObject();
            }

            public Result Convert()
            {
                var target = new JsonObject { ["asyncapi"] = Latest };
                _changes.Add($"asyncapi: {_version} to {Latest}");
                Copy(target, "id");

                // Moved metadata: tags and externalDocs go into info.
                var info = _source["info"]?.DeepClone() as JsonObject ?? new JsonObject();
                foreach (var name in new[] { "tags", "externalDocs" })
                {
                    if (_source[name] is JsonNode moved)
                    {
                        info[name] = moved.DeepClone();
                        _changes.Add($"{name} moved into info");
                    }
                }

                target["info"] = info;
                Copy(target, "defaultContentType");

                if (_source["servers"] is JsonObject servers)
                {
                    target["servers"] = Servers(servers);
                }

                var channels = new JsonObject();
                var operations = new JsonObject();
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var pair in _source["channels"] as JsonObject ?? new JsonObject())
                {
                    Channel(pair.Key, pair.Value as JsonObject, channels, operations, ids);
                }

                if (channels.Count > 0)
                {
                    target["channels"] = channels;
                }

                if (operations.Count > 0)
                {
                    target["operations"] = operations;
                }

                var components = Components();
                if (components.Count > 0)
                {
                    target["components"] = components;
                }

                foreach (var pair in _source.Where(p => p.Key.StartsWith("x-", StringComparison.Ordinal)))
                {
                    target[pair.Key] = pair.Value?.DeepClone();
                }

                return new Result(target, _changes, _losses);
            }

            /// <summary>Server URL splitting up; security by explicit reference, with its scopes on the scheme.</summary>
            private JsonObject Servers(JsonObject servers)
            {
                var result = new JsonObject();
                foreach (var pair in servers)
                {
                    if (!(pair.Value is JsonObject server))
                    {
                        continue;
                    }

                    var converted = new JsonObject();
                    var url = server["url"]?.GetValue<string>() ?? string.Empty;
                    var rest = url.IndexOf("://", StringComparison.Ordinal) is var scheme && scheme >= 0 ? url.Substring(scheme + 3) : url;
                    var slash = rest.IndexOf('/');
                    converted["host"] = slash >= 0 ? rest.Substring(0, slash) : rest;
                    if (slash >= 0 && rest.Length > slash + 1)
                    {
                        converted["pathname"] = rest.Substring(slash);
                    }

                    foreach (var property in server.Where(p => p.Key != "url" && p.Key != "security"))
                    {
                        converted[property.Key] = property.Value?.DeepClone();
                    }

                    if (server["security"] is JsonArray security)
                    {
                        converted["security"] = Security(security, $"server {pair.Key}");
                    }

                    _changes.Add($"server {pair.Key}: url split into host{(converted["pathname"] is null ? string.Empty : " and pathname")}");
                    result[pair.Key] = converted;
                }

                return result;
            }

            /// <summary>v2's security requirements by name, as explicit references; scopes move to the scheme.</summary>
            private JsonArray Security(JsonArray requirements, string where)
            {
                var result = new JsonArray();
                foreach (var requirement in requirements.OfType<JsonObject>())
                {
                    foreach (var pair in requirement)
                    {
                        result.Add(new JsonObject { ["$ref"] = $"#/components/securitySchemes/{pair.Key}" });
                        if (pair.Value is JsonArray { Count: > 0 } scopes && _securitySchemes[pair.Key] is JsonObject scheme)
                        {
                            var existing = scheme["scopes"] as JsonArray ?? new JsonArray();
                            foreach (var scope in scopes.Select(s => s?.GetValue<string>()).Where(s => s != null && existing.All(e => e?.GetValue<string>() != s)))
                            {
                                existing.Add(scope);
                            }

                            scheme["scopes"] = existing;
                            _losses.Add($"{where}: the scopes it required of {pair.Key} are now the scheme's scopes, for every server and operation that uses it");
                        }
                    }
                }

                return result;
            }

            /// <summary>A channel keyed by its id with an address; its publish and subscribe as operations.</summary>
            private void Channel(string address, JsonObject? channel, JsonObject channels, JsonObject operations, HashSet<string> ids)
            {
                if (channel is null)
                {
                    return;
                }

                if (channel["$ref"] != null)
                {
                    _losses.Add($"channel {address}: a channel given by $ref isn't converted; inline it first");
                    return;
                }

                var id = Unique(Identifier(address) + "Channel", ids);
                var converted = new JsonObject { ["address"] = address };
                _changes.Add($"channel {address}: keyed as {id}, its path now the address");
                foreach (var property in channel.Where(p => p.Key is "description" or "bindings" || p.Key.StartsWith("x-", StringComparison.Ordinal)))
                {
                    converted[property.Key] = property.Value?.DeepClone();
                }

                if (channel["servers"] is JsonArray servers)
                {
                    converted["servers"] = new JsonArray(servers.Select(s => (JsonNode?)new JsonObject { ["$ref"] = $"#/servers/{s?.GetValue<string>()}" }).ToArray());
                }

                if (channel["parameters"] is JsonObject parameters)
                {
                    var converts = new JsonObject();
                    foreach (var pair in parameters)
                    {
                        converts[pair.Key] = Parameter(pair.Value, $"channel {address}, parameter {pair.Key}");
                    }

                    converted["parameters"] = converts;
                }

                var messages = new JsonObject();
                converted["messages"] = messages;
                channels[id] = converted;

                // Operation keywords: v2's publish means others publish to this application, which receives.
                foreach (var (keyword, action) in new[] { ("publish", "receive"), ("subscribe", "send") })
                {
                    if (!(channel[keyword] is JsonObject operation))
                    {
                        continue;
                    }

                    var operationId = operation["operationId"]?.GetValue<string>() ?? Unique(Identifier(keyword + " " + address), ids);
                    var result = new JsonObject
                    {
                        ["action"] = action,
                        ["channel"] = new JsonObject { ["$ref"] = $"#/channels/{id}" },
                    };
                    foreach (var property in operation.Where(p => p.Key is "summary" or "description" or "tags" or "externalDocs" or "bindings"
                                                                   || p.Key.StartsWith("x-", StringComparison.Ordinal)))
                    {
                        result[property.Key] = property.Value?.DeepClone();
                    }

                    if (operation["traits"] is JsonArray traits)
                    {
                        result["traits"] = traits.DeepClone();
                        _changes.Add($"operation {operationId}: its traits no longer override its own properties (a 3.0 rule)");
                    }

                    if (operation["security"] is JsonArray security)
                    {
                        result["security"] = Security(security, $"operation {operationId}");
                    }

                    var references = new JsonArray();
                    var listed = operation["message"] is JsonObject { } message && message["oneOf"] is JsonArray oneOf
                        ? oneOf.OfType<JsonObject>().ToList()
                        : operation["message"] is JsonObject single ? new List<JsonObject> { single } : new List<JsonObject>();
                    for (var i = 0; i < listed.Count; i++)
                    {
                        var key = MessageKey(listed[i], operationId, i, listed.Count);
                        if (!messages.ContainsKey(key))
                        {
                            messages[key] = listed[i]["$ref"] is JsonNode reference
                                ? new JsonObject { ["$ref"] = reference.DeepClone() }
                                : Message(listed[i], $"operation {operationId}, message {key}");
                        }

                        references.Add(new JsonObject { ["$ref"] = $"#/channels/{id}/messages/{Pointer(key)}" });
                    }

                    if (references.Count > 0)
                    {
                        result["messages"] = references;
                    }

                    operations[operationId] = result;
                    _changes.Add($"{keyword} on {address}: operation {operationId}, action {action}");
                }
            }

            /// <summary>A message: its id is its key now, and a schemaFormat moves onto the payload as a multi-format schema.</summary>
            private JsonObject Message(JsonObject message, string where)
            {
                var result = new JsonObject();
                foreach (var property in message.Where(p => p.Key != "messageId" && p.Key != "schemaFormat"))
                {
                    result[property.Key] = property.Value?.DeepClone();
                }

                if (message["schemaFormat"]?.GetValue<string>() is string format && result["payload"] is JsonNode payload)
                {
                    result["payload"] = new JsonObject { ["schemaFormat"] = format, ["schema"] = payload.DeepClone() };
                    _changes.Add($"{where}: schemaFormat moved onto the payload");
                }

                if (message["traits"] != null)
                {
                    _changes.Add($"{where}: its traits no longer override its own properties (a 3.0 rule)");
                }

                return result;
            }

            /// <summary>A channel parameter: 3.0 keeps enum, default, description, examples and location, always as strings.</summary>
            private JsonNode? Parameter(JsonNode? parameter, string where)
            {
                if (!(parameter is JsonObject value) || value["$ref"] != null)
                {
                    return parameter?.DeepClone();
                }

                var result = new JsonObject();
                foreach (var name in new[] { "description", "location" })
                {
                    if (value[name] is JsonNode kept)
                    {
                        result[name] = kept.DeepClone();
                    }
                }

                if (value["schema"] is JsonObject schema)
                {
                    foreach (var name in new[] { "enum", "default", "examples" })
                    {
                        if (schema[name] is JsonNode kept)
                        {
                            result[name] = kept.DeepClone();
                        }
                    }

                    var dropped = schema.Select(p => p.Key).Where(k => k is not ("enum" or "default" or "examples" or "description")
                                                                       && !(k == "type" && schema["type"]?.GetValue<string>() == "string")).ToList();
                    if (dropped.Count > 0)
                    {
                        _losses.Add($"{where}: {string.Join(", ", dropped)} has no place in 3.0, whose parameters are strings with enum, default, description, examples and location");
                    }
                }

                return result;
            }

            private JsonObject Components()
            {
                var source = _source["components"] as JsonObject ?? new JsonObject();
                var result = new JsonObject();
                foreach (var pair in source)
                {
                    switch (pair.Key)
                    {
                        case "messages" when pair.Value is JsonObject messages:
                            var converted = new JsonObject();
                            foreach (var message in messages)
                            {
                                converted[message.Key] = message.Value is JsonObject m && m["$ref"] == null ? Message(m, $"component message {message.Key}") : message.Value?.DeepClone();
                            }

                            result["messages"] = converted;
                            break;
                        case "parameters" when pair.Value is JsonObject parameters:
                            var converts = new JsonObject();
                            foreach (var parameter in parameters)
                            {
                                converts[parameter.Key] = Parameter(parameter.Value, $"component parameter {parameter.Key}");
                            }

                            result["parameters"] = converts;
                            break;
                        case "securitySchemes":
                            result["securitySchemes"] = _securitySchemes;
                            break;
                        default:
                            result[pair.Key] = pair.Value?.DeepClone();
                            break;
                    }
                }

                return result;
            }

            private static string MessageKey(JsonObject message, string operationId, int index, int count)
            {
                if (message["$ref"]?.GetValue<string>() is string reference)
                {
                    return reference.Substring(reference.LastIndexOf('/') + 1);
                }

                return message["messageId"]?.GetValue<string>()
                       ?? message["name"]?.GetValue<string>()
                       ?? (count == 1 ? operationId + "Message" : operationId + "Message" + (index + 1).ToString(CultureInfo.InvariantCulture));
            }

            private void Copy(JsonObject target, string name)
            {
                if (_source[name] is JsonNode value)
                {
                    target[name] = value.DeepClone();
                }
            }

            private static string Pointer(string key) => key.Replace("~", "~0").Replace("/", "~1");

            private static string Unique(string id, HashSet<string> ids)
            {
                var candidate = id;
                for (var n = 2; !ids.Add(candidate); n++)
                {
                    candidate = id + n.ToString(CultureInfo.InvariantCulture);
                }

                return candidate;
            }

            /// <summary>An identifier from an address: its words in camel case (<c>user/signedup</c> is <c>userSignedup</c>).</summary>
            private static string Identifier(string text)
            {
                var words = new List<string>();
                var word = new StringBuilder();
                foreach (var c in text)
                {
                    if (char.IsLetterOrDigit(c))
                    {
                        word.Append(c);
                    }
                    else if (word.Length > 0)
                    {
                        words.Add(word.ToString());
                        word.Clear();
                    }
                }

                if (word.Length > 0)
                {
                    words.Add(word.ToString());
                }

                if (words.Count == 0)
                {
                    return "root";
                }

                var result = new StringBuilder(char.ToLowerInvariant(words[0][0]) + words[0].Substring(1));
                foreach (var w in words.Skip(1))
                {
                    result.Append(char.ToUpperInvariant(w[0])).Append(w.Substring(1));
                }

                return char.IsDigit(result[0]) ? "c" + result : result.ToString();
            }
        }
    }
}
