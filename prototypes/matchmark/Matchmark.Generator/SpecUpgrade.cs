using System;
using Matchmark.Specs.AsyncApi;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;

namespace Matchmark.Generator
{
    /// <summary>
    /// A specification forward-ported to the latest version of its format (decisions Q22 to Q25), or written down to an
    /// older one a consumer needs: OpenAPI (to 3.2, or 3.1), AsyncAPI (to 3.1), Arazzo (to 1.1) and Overlay (to 1.2).
    /// Every change is listed, and so is anything the target version has no place for.
    /// </summary>
    public static class SpecUpgrade
    {
        public sealed class Result
        {
            internal Result(string format, string from, string to, JsonObject document, IReadOnlyList<string> changes, IReadOnlyList<string> losses)
            {
                Format = format;
                From = from;
                To = to;
                Document = document;
                Changes = changes;
                Losses = losses;
            }

            public string Format { get; }

            public string From { get; }

            public string To { get; }

            public JsonObject Document { get; }

            public IReadOnlyList<string> Changes { get; }

            public IReadOnlyList<string> Losses { get; }
        }

        /// <summary>The document at <paramref name="to"/>, or at the latest version of its format.</summary>
        public static Result Upgrade(string text, string path, string? to = null)
        {
            var document = Overlay.Parse(text, path) as JsonObject ?? throw new InvalidOperationException($"{path} isn't an object.");
            if (document["asyncapi"] is JsonNode asyncApi)
            {
                Only(to, "AsyncAPI", AsyncApiUpgrade.Latest);
                var result = AsyncApiUpgrade.Upgrade(document);
                return new Result("AsyncAPI", asyncApi.GetValue<string>(), AsyncApiUpgrade.Latest, result.Document, result.Changes, result.Losses);
            }

            if (document["arazzo"] is JsonNode arazzo)
            {
                Only(to, "Arazzo", "1.1.0");
                return Arazzo(document, arazzo.GetValue<string>());
            }

            if (document["overlay"] is JsonNode overlay)
            {
                Only(to, "Overlay", "1.2.0");
                return Simple("Overlay", "overlay", document, overlay.GetValue<string>(), "1.2.0",
                    "Overlay 1.2 adds to earlier versions without changing them; targets were RFC 9535 JSONPath from 1.0");
            }

            return OpenApi(text, path, document, to);
        }

        private static Result OpenApi(string text, string path, JsonObject source, string? to)
        {
            var from = source["openapi"]?.GetValue<string>() ?? (source["swagger"] is JsonNode swagger ? swagger.GetValue<string>() : "unknown");
            var target = to ?? "3.2.0";
            if (!target.StartsWith("3.2", StringComparison.Ordinal) && !target.StartsWith("3.1", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"OpenAPI can be written as 3.2 or 3.1, not {target}.");
            }

            var document = SpecReader.Read(text, path, out var problems)
                           ?? throw new InvalidOperationException($"{path} could not be read: {string.Join("; ", problems)}");
            var threeTwo = target.StartsWith("3.2", StringComparison.Ordinal);
            var written = JsonNode.Parse(SpecJson.Write(document, threeTwo)) as JsonObject ?? new JsonObject();
            var writtenVersion = written["openapi"]?.GetValue<string>() ?? target;
            var changes = from == writtenVersion ? new List<string>() : new List<string> { $"openapi: {from} to {writtenVersion}" };
            if (source["swagger"] != null)
            {
                changes.Add("Swagger 2.0 converted by Microsoft.OpenApi: definitions to components.schemas, body and formData parameters to requestBody, produces and consumes to content, host and basePath to servers");
            }

            var losses = problems.Select(p => $"the reader objected, so part of the spec may be missing: {p}").ToList();
            var before = Operations(source);
            var after = Operations(written);
            foreach (var missing in before.Except(after))
            {
                losses.Add($"{missing} isn't in the written document");
            }

            // Written down to 3.1, an operation 3.1 has no method for (QUERY) goes into x-oai-additionalOperations, as
            // ASP.NET Core writes it.
            foreach (var moved in Operations(written, "x-oai-additionalOperations"))
            {
                changes.Add($"{moved} written as x-oai-additionalOperations, since {written["openapi"]} has no {moved.Split(' ')[0]} method");
            }

            return new Result("OpenAPI", from, written["openapi"]?.GetValue<string>() ?? target, written, changes, losses);
        }

        /// <summary>Arazzo 1.1 reads JSONPath as RFC 9535 by default; 1.0's meant the Goessner draft, so it's kept by naming it.</summary>
        private static Result Arazzo(JsonObject source, string from)
        {
            var document = (JsonObject)source.DeepClone();
            var changes = new List<string>();
            if (from.StartsWith("1.0", StringComparison.Ordinal))
            {
                document["arazzo"] = "1.1.0";
                changes.Add($"arazzo: {from} to 1.1.0");
                foreach (var criterion in Criteria(document))
                {
                    if (criterion["type"] is JsonValue type && type.GetValueKind() == JsonValueKind.String && type.GetValue<string>() == "jsonpath")
                    {
                        criterion["type"] = new JsonObject { ["type"] = "jsonpath", ["version"] = "draft-goessner-dispatch-jsonpath-00" };
                        changes.Add($"criterion '{criterion["condition"]}': JSONPath named as the Goessner draft 1.0 meant, since 1.1 reads RFC 9535 by default");
                    }
                }
            }

            return new Result("Arazzo", from, document["arazzo"]!.GetValue<string>(), document, changes, new List<string>());
        }

        private static Result Simple(string format, string field, JsonObject source, string from, string to, string note)
        {
            var document = (JsonObject)source.DeepClone();
            var changes = new List<string>();
            if (from != to)
            {
                document[field] = to;
                changes.Add($"{field}: {from} to {to} ({note})");
            }

            return new Result(format, from, to, document, changes, new List<string>());
        }

        private static IEnumerable<JsonObject> Criteria(JsonNode? node)
        {
            switch (node)
            {
                case JsonObject obj:
                    foreach (var pair in obj)
                    {
                        if (pair.Key is "successCriteria" or "criteria" && pair.Value is JsonArray list)
                        {
                            foreach (var criterion in list.OfType<JsonObject>())
                            {
                                yield return criterion;
                            }
                        }
                        else
                        {
                            foreach (var inner in Criteria(pair.Value))
                            {
                                yield return inner;
                            }
                        }
                    }

                    break;
                case JsonArray array:
                    foreach (var inner in array.SelectMany(Criteria))
                    {
                        yield return inner;
                    }

                    break;
            }
        }

        private static HashSet<string> Operations(JsonObject document, string additional)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in document["paths"] as JsonObject ?? new JsonObject())
            {
                foreach (var method in path.Value?[additional] as JsonObject ?? new JsonObject())
                {
                    result.Add($"{method.Key.ToUpperInvariant()} {path.Key}");
                }
            }

            return result;
        }

        private static HashSet<string> Operations(JsonObject document)
        {
            var methods = new[] { "get", "put", "post", "delete", "options", "head", "patch", "trace", "query" };
            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in document["paths"] as JsonObject ?? new JsonObject())
            {
                foreach (var method in (path.Value as JsonObject ?? new JsonObject()).Where(m => methods.Contains(m.Key)))
                {
                    result.Add($"{method.Key.ToUpperInvariant()} {path.Key}");
                }

                foreach (var extra in new[] { "additionalOperations", "x-oai-additionalOperations" })
                {
                    foreach (var method in path.Value?[extra] as JsonObject ?? new JsonObject())
                    {
                        result.Add($"{method.Key.ToUpperInvariant()} {path.Key}");
                    }
                }
            }

            return result;
        }

        private static void Only(string? to, string format, string latest)
        {
            if (to != null && to != latest && !latest.StartsWith(to + ".", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{format} is written only as {latest}.");
            }
        }

        /// <summary>A JSON document as YAML: block style, strings in double quotes where plain text would be read differently.</summary>
        public static string Yaml(JsonNode? node)
        {
            var text = new StringBuilder();
            WriteYaml(text, node, 0, false);
            return text.ToString();
        }

        private static void WriteYaml(StringBuilder text, JsonNode? node, int indent, bool inList)
        {
            var pad = new string(' ', indent);
            switch (node)
            {
                case JsonObject obj when obj.Count == 0:
                    text.Append(" {}\n");
                    break;
                case JsonObject obj:
                    if (!inList)
                    {
                        text.Append('\n');
                    }

                    var first = true;
                    foreach (var pair in obj)
                    {
                        text.Append(inList && first ? string.Empty : pad).Append(Key(pair.Key)).Append(':');
                        first = false;
                        if (pair.Value is JsonObject or JsonArray)
                        {
                            WriteYaml(text, pair.Value, indent + 2, false);
                        }
                        else
                        {
                            text.Append(' ').Append(Scalar(pair.Value)).Append('\n');
                        }
                    }

                    break;
                case JsonArray array when array.Count == 0:
                    text.Append(" []\n");
                    break;
                case JsonArray array:
                    text.Append('\n');
                    foreach (var item in array)
                    {
                        text.Append(pad).Append("- ");
                        if (item is JsonObject { Count: > 0 })
                        {
                            WriteYaml(text, item, indent + 2, true);
                        }
                        else if (item is JsonArray { Count: > 0 })
                        {
                            WriteYaml(text, item, indent + 2, false);
                        }
                        else
                        {
                            text.Append(item is JsonObject ? "{}" : item is JsonArray ? "[]" : Scalar(item)).Append('\n');
                        }
                    }

                    break;
                default:
                    text.Append(Scalar(node)).Append('\n');
                    break;
            }
        }

        private static string Key(string key)
            => key.Length > 0 && key.All(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.' or '$' or '/') && !char.IsDigit(key[0]) && key[0] != '-'
                ? key
                : JsonSerializer.Serialize(key);

        private static string Scalar(JsonNode? value)
            => value is null ? "null"
                : value.GetValueKind() == JsonValueKind.String ? JsonSerializer.Serialize(value.GetValue<string>(), Options)
                : value.ToJsonString();

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    }
}
