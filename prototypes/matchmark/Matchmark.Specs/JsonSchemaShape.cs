using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Matchmark.Specs
{
    /// <summary>
    /// Whether a JSON Schema (2020-12) is well formed, as its metaschema judges one: a schema is an object or a boolean,
    /// and each keyword it uses has the type the metaschema gives it (<c>type</c> one of the seven names, <c>required</c>
    /// unique strings, <c>properties</c> an object of schemas, <c>minLength</c> a non-negative integer, ...). Keywords it
    /// doesn't know are allowed, as the metaschema allows them. Used wherever a specification embeds a schema.
    /// </summary>
    /// <summary>The JSON Schema dialect a specification embeds.</summary>
    public enum SchemaDialect
    {
        /// <summary>JSON Schema 2020-12 (Arazzo, OpenAPI 3.1).</summary>
        Draft202012,

        /// <summary>AsyncAPI's Schema Object: JSON Schema draft-07, with <c>discriminator</c>, <c>externalDocs</c> and <c>deprecated</c>.</summary>
        AsyncApi,
    }

    public static class JsonSchemaShape
    {
        private static readonly string[] Newer = { "prefixItems", "$defs", "dependentSchemas", "dependentRequired", "unevaluatedItems", "unevaluatedProperties", "$anchor", "$dynamicRef", "$dynamicAnchor", "$vocabulary", "maxContains", "minContains", "contentSchema" };

        private static readonly string[] Types = { "null", "boolean", "object", "array", "number", "string", "integer" };
        private static readonly string[] Strings = { "$id", "$schema", "$ref", "$anchor", "$dynamicRef", "$dynamicAnchor", "$comment", "title", "description", "format", "pattern", "contentEncoding", "contentMediaType" };
        private static readonly string[] Schemas = { "additionalProperties", "propertyNames", "items", "contains", "not", "if", "then", "else", "unevaluatedItems", "unevaluatedProperties", "contentSchema", "additionalItems" };
        private static readonly string[] SchemaMaps = { "properties", "patternProperties", "$defs", "definitions", "dependentSchemas" };
        private static readonly string[] SchemaLists = { "prefixItems", "allOf", "anyOf", "oneOf" };
        private static readonly string[] Counts = { "maxLength", "minLength", "maxItems", "minItems", "maxProperties", "minProperties", "maxContains", "minContains" };
        private static readonly string[] Numbers = { "maximum", "minimum", "exclusiveMaximum", "exclusiveMinimum" };
        private static readonly string[] Flags = { "uniqueItems", "deprecated", "readOnly", "writeOnly" };

        public static void Check(Shape shape, JsonNode? node, string pointer, int depth = 0) => Check(shape, node, pointer, SchemaDialect.Draft202012, depth);

        public static void Check(Shape shape, JsonNode? node, string pointer, SchemaDialect dialect, int depth = 0)
        {
            if (node is JsonValue flag && flag.GetValueKind() is JsonValueKind.True or JsonValueKind.False)
            {
                return;
            }

            if (node is not JsonObject schema)
            {
                shape.Add(pointer, "A schema must be an object or a boolean.");
                return;
            }

            if (depth > 64)
            {
                return;
            }

            foreach (var pair in schema)
            {
                var at = Pointer.Join(pointer, pair.Key);
                var value = pair.Value;
                var kind = value?.GetValueKind() ?? JsonValueKind.Null;
                if (dialect == SchemaDialect.AsyncApi && Array.IndexOf(Newer, pair.Key) >= 0)
                {
                    continue; // Keywords draft-07 doesn't have: unknown to it, so any value is allowed.
                }

                if (dialect == SchemaDialect.AsyncApi && pair.Key == "items" && value is JsonArray tuple)
                {
                    // draft-07: a list of schemas, one per position, at least one.
                    if (tuple.Count == 0)
                    {
                        shape.Add(at, "'items' as a list must hold at least one schema.");
                    }

                    for (var i = 0; i < tuple.Count; i++)
                    {
                        Check(shape, tuple[i], Pointer.Join(at, i), dialect, depth + 1);
                    }

                    continue;
                }

                if (dialect == SchemaDialect.AsyncApi && pair.Key == "discriminator")
                {
                    if (kind != JsonValueKind.String)
                    {
                        shape.Add(at, "'discriminator' must be a string (the property that names the schema).");
                    }

                    continue;
                }

                if (dialect == SchemaDialect.AsyncApi && pair.Key == "externalDocs")
                {
                    // An External Documentation Object (a url, a description), or a Reference to one.
                    if (value is JsonObject docs && !docs.ContainsKey("$ref"))
                    {
                        shape.Only(docs, at, "An External Documentation Object", "url", "description");
                        shape.String(docs, "url", at, required: true);
                        shape.String(docs, "description", at);
                    }
                    else if (value is not JsonObject)
                    {
                        shape.Add(at, "'externalDocs' must be an object.");
                    }

                    continue;
                }

                if (pair.Key == "type")
                {
                    if (!(kind == JsonValueKind.String && Array.IndexOf(Types, value!.GetValue<string>()) >= 0
                          || value is JsonArray list && list.Count > 0 && list.All(t => t is JsonValue v && v.GetValueKind() == JsonValueKind.String && Array.IndexOf(Types, v.GetValue<string>()) >= 0)
                             && list.Select(t => t!.GetValue<string>()).Distinct().Count() == list.Count))
                    {
                        shape.Add(at, "'type' must be one of null, boolean, object, array, number, string, integer, or a list of them.");
                    }
                }
                else if (Array.IndexOf(Strings, pair.Key) >= 0)
                {
                    if (kind != JsonValueKind.String)
                    {
                        shape.Add(at, $"'{pair.Key}' must be a string.");
                    }
                }
                else if (Array.IndexOf(Schemas, pair.Key) >= 0)
                {
                    Check(shape, value, at, dialect, depth + 1);
                }
                else if (Array.IndexOf(SchemaMaps, pair.Key) >= 0)
                {
                    if (value is JsonObject map)
                    {
                        foreach (var entry in map)
                        {
                            Check(shape, entry.Value, Pointer.Join(at, entry.Key), dialect, depth + 1);
                        }
                    }
                    else
                    {
                        shape.Add(at, $"'{pair.Key}' must be an object of schemas.");
                    }
                }
                else if (Array.IndexOf(SchemaLists, pair.Key) >= 0)
                {
                    if (value is JsonArray list && list.Count > 0)
                    {
                        for (var i = 0; i < list.Count; i++)
                        {
                            Check(shape, list[i], Pointer.Join(at, i), dialect, depth + 1);
                        }
                    }
                    else
                    {
                        shape.Add(at, $"'{pair.Key}' must be a non-empty list of schemas.");
                    }
                }
                else if (Array.IndexOf(Counts, pair.Key) >= 0)
                {
                    if (!(JsonNumber.Decimal(value) is { } n && n >= 0 && n == decimal.Truncate(n)))
                    {
                        shape.Add(at, $"'{pair.Key}' must be a non-negative integer.");
                    }
                }
                else if (Array.IndexOf(Numbers, pair.Key) >= 0)
                {
                    if (kind != JsonValueKind.Number)
                    {
                        shape.Add(at, $"'{pair.Key}' must be a number.");
                    }
                }
                else if (pair.Key == "multipleOf")
                {
                    if (!(JsonNumber.Decimal(value) is { } n && n > 0))
                    {
                        shape.Add(at, "'multipleOf' must be a number above 0.");
                    }
                }
                else if (Array.IndexOf(Flags, pair.Key) >= 0)
                {
                    if (kind is not (JsonValueKind.True or JsonValueKind.False))
                    {
                        shape.Add(at, $"'{pair.Key}' must be true or false.");
                    }
                }
                else if (pair.Key is "required")
                {
                    if (!UniqueStrings(value))
                    {
                        shape.Add(at, "'required' must be a list of distinct strings.");
                    }
                }
                else if (pair.Key is "enum" or "examples")
                {
                    if (value is not JsonArray list)
                    {
                        shape.Add(at, $"'{pair.Key}' must be a list.");
                    }
                    else if (dialect == SchemaDialect.AsyncApi && pair.Key == "enum")
                    {
                        // AsyncAPI's draft-07 metaschema: at least one value, each once.
                        if (list.Count == 0)
                        {
                            shape.Add(at, "'enum' must hold at least one value.");
                        }

                        shape.Unique(list, at, "enum");
                    }
                }
                else if (pair.Key is "dependentRequired")
                {
                    if (!(value is JsonObject map && map.All(e => UniqueStrings(e.Value))))
                    {
                        shape.Add(at, "'dependentRequired' must be an object of lists of distinct strings.");
                    }
                }
                else if (pair.Key is "$vocabulary")
                {
                    if (!(value is JsonObject map && map.All(e => e.Value?.GetValueKind() is JsonValueKind.True or JsonValueKind.False)))
                    {
                        shape.Add(at, "'$vocabulary' must be an object of true or false.");
                    }
                }
                else if (pair.Key is "dependencies")
                {
                    if (value is JsonObject map)
                    {
                        foreach (var entry in map.Where(e => e.Value is not JsonArray))
                        {
                            Check(shape, entry.Value, Pointer.Join(at, entry.Key), dialect, depth + 1);
                        }
                    }
                    else
                    {
                        shape.Add(at, "'dependencies' must be an object.");
                    }
                }
            }
        }

        private static bool UniqueStrings(JsonNode? node)
            => node is JsonArray list && list.All(i => i is JsonValue v && v.GetValueKind() == JsonValueKind.String)
               && list.Select(i => i!.GetValue<string>()).Distinct(StringComparer.Ordinal).Count() == list.Count;
    }

    /// <summary>JSON numbers as decimals, however they were read (a long from YAML, a decimal, a double from JSON).</summary>
    public static class JsonNumber
    {
        public static decimal? Decimal(JsonNode? node)
            => node is JsonValue v && v.GetValueKind() == JsonValueKind.Number
               && decimal.TryParse(v.ToJsonString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)
                ? d
                : null;
    }
}
