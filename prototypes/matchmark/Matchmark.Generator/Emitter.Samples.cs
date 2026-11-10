using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.OpenApi;

namespace Matchmark.Generator
{
    /// <summary>The emitter's samples (decision Q10: the model and its outputs, split by output).</summary>
    internal sealed partial class Emitter
    {
        // ---------------------------------------------------------------- samples

        /// <summary>
        /// A JSON body for a response: the media type's example, else the schema's, else one built from the schema that
        /// passes its own constraints (required properties, lengths, ranges, the first enum value, the first oneOf member).
        /// </summary>
        private string? Sample(IOpenApiMediaType media, string context)
        {
            var example = media.Example ?? media.Examples?.Values.Select(e => e.Value).FirstOrDefault(v => v is not null);
            var node = example?.DeepClone() ?? SampleOf(media.Schema, context, 0);
            return node?.ToJsonString();
        }

        private System.Text.Json.Nodes.JsonNode? SampleOf(IOpenApiSchema? schema, string context, int depth)
        {
            if (schema is null || depth > 8)
            {
                return null;
            }

            if (ExampleOf(schema) is { } example)
            {
                return example.DeepClone();
            }

            if (RefId(schema) is { } id)
            {
                if (id == "ProblemDetails")
                {
                    return new System.Text.Json.Nodes.JsonObject { ["title"] = "Example problem" };
                }

                if (id == ValidationSchema)
                {
                    return new System.Text.Json.Nodes.JsonObject { ["title"] = "One or more validation errors occurred.", ["errors"] = new System.Text.Json.Nodes.JsonObject() };
                }

                if (!_schemas.TryGetValue(id, out var target))
                {
                    return null;
                }

                if (ExampleOf(target) is { } targetExample)
                {
                    return targetExample.DeepClone();
                }

                if (target.OneOf is { Count: > 0 } && target.OneOf[0] is OpenApiSchemaReference first)
                {
                    return SampleOf(first, context, depth + 1);
                }

                if (IsEnum(target))
                {
                    return System.Text.Json.Nodes.JsonValue.Create(target.Enum![0].GetValue<string>());
                }

                var obj = new System.Text.Json.Nodes.JsonObject();
                _unionMembers.TryGetValue(id, out var union);
                foreach (var property in target.Properties ?? new Dictionary<string, IOpenApiSchema>())
                {
                    if (union.Property == property.Key)
                    {
                        obj[property.Key] = union.Value;
                    }
                    else if (target.Required?.Contains(property.Key) == true)
                    {
                        obj[property.Key] = Has(property.Value, JsonSchemaType.Null) ? null : SampleOf(property.Value, $"{context} property '{id}.{property.Key}'", depth + 1);
                    }
                }

                return obj;
            }

            var type = (schema.Type ?? 0) & ~JsonSchemaType.Null;
            switch (type)
            {
                case JsonSchemaType.String:
                    var text = schema.Format switch
                    {
                        "date-time" => "2026-01-01T00:00:00Z",
                        "date" => "2026-01-01",
                        "uuid" => "00000000-0000-0000-0000-000000000001",
                        _ => new string('x', Math.Max(1, Math.Min(schema.MinLength ?? 1, schema.MaxLength ?? int.MaxValue))),
                    };
                    if (schema.Pattern is { } pattern && !System.Text.RegularExpressions.Regex.IsMatch(text, pattern))
                    {
                        Diagnostics.Add(Diagnostic.Create(Generator.Diagnostics.UnsampleableString, _location, context));
                    }

                    return System.Text.Json.Nodes.JsonValue.Create(text);
                case JsonSchemaType.Integer:
                case JsonSchemaType.Number:
                    var value = decimal.TryParse(schema.Minimum, NumberStyles.Float, CultureInfo.InvariantCulture, out var min) ? Math.Max(min, 1) : 1m;
                    if (decimal.TryParse(schema.Maximum, NumberStyles.Float, CultureInfo.InvariantCulture, out var max))
                    {
                        value = Math.Min(value, max);
                    }

                    return type == JsonSchemaType.Integer
                        ? System.Text.Json.Nodes.JsonValue.Create((long)value)
                        : System.Text.Json.Nodes.JsonValue.Create(value);
                case JsonSchemaType.Boolean:
                    return System.Text.Json.Nodes.JsonValue.Create(true);
                case JsonSchemaType.Array:
                    return new System.Text.Json.Nodes.JsonArray(SampleOf(schema.Items, context, depth + 1));
                case JsonSchemaType.Object:
                    return new System.Text.Json.Nodes.JsonObject();
                default:
                    return null;
            }
        }

        /// <summary>A schema's example: OpenAPI 3.1's <c>examples</c>, or the older single <c>example</c>.</summary>
        private static System.Text.Json.Nodes.JsonNode? ExampleOf(IOpenApiSchema schema)
        {
            if (schema.Examples is { Count: > 0 } examples)
            {
                return examples[0];
            }

#pragma warning disable CS0618 // Still how a 3.0-style `example` arrives.
            return schema.Example;
#pragma warning restore CS0618
        }
    }
}
