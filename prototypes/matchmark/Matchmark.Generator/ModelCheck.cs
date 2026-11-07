using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.OpenApi;

namespace Matchmark.Generator
{
    /// <summary>
    /// Comparing a C# type's JSON shape with the schema it stands for. The types generated from the spec match it by
    /// construction; any other type is compared property by property, as System.Text.Json would read and write it.
    /// </summary>
    internal sealed partial class Emitter
    {
        /// <summary>"Pet", "IReadOnlyList&lt;Pet&gt;", "string": how a schema is named in messages.</summary>
        public static string SchemaName(IOpenApiSchema schema)
            => RefId(schema) ?? (Has(schema, JsonSchemaType.Array) && schema.Items is { } items ? SchemaName(items) + "[]" : Canonical(schema) ?? "object");

        /// <summary>The type generated from <paramref name="schema"/>, or an array of it, or the primitive it is.</summary>
        public bool ByConstruction(IOpenApiSchema schema, string actual, IReadOnlyDictionary<string, TypeShape> shapes)
        {
            if (RefId(schema) is { } id)
            {
                // A framework type stands for its schema, and so does anything derived from it (ValidationProblemDetails).
                var generated = Generated(id);
                return actual == generated
                    || (WellKnownSchemas.ContainsKey(id) && shapes.TryGetValue(actual, out var derived) && derived.Bases.Contains(generated));
            }

            if (Has(schema, JsonSchemaType.Array) && schema.Items is { } items)
            {
                return shapes.TryGetValue(actual, out var shape) && shape.Kind == "Array" && shape.Element is { } element
                    && ByConstruction(items, element, shapes);
            }

            // A primitive isn't generated: it is consistent when it is the primitive the schema says.
            return Canonical(schema) is not { } primitive
                   || (shapes.TryGetValue(actual, out var p) && p.Kind == "Primitive" && p.Primitive == primitive);
        }

        public List<string> Differences(IOpenApiSchema schema, string actual, IReadOnlyDictionary<string, TypeShape> shapes)
        {
            var differences = new List<string>();
            Compare(schema, actual, shapes, string.Empty, differences, new HashSet<string>());
            return differences;
        }

        /// <summary>The full name of the type generated from a component schema, or the framework type it maps to.</summary>
        private string Generated(string id)
            => WellKnownSchemas.TryGetValue(id, out var wellKnown) ? wellKnown.Replace("global::", string.Empty) : $"{_namespace}.{Naming.Pascal(id)}";

        private void Compare(IOpenApiSchema schema, string actual, IReadOnlyDictionary<string, TypeShape> shapes, string path, List<string> differences, HashSet<string> visited)
        {
            var where = path.Length == 0 ? "the type" : path;
            if (!shapes.TryGetValue(actual, out var shape))
            {
                differences.Add($"{where} is {Short(actual)}, which can't be read");
                return;
            }

            if (RefId(schema) is { } id)
            {
                var generated = Generated(id);
                if (actual == generated || shape.Bases.Contains(generated) || !visited.Add($"{id}|{actual}"))
                {
                    return;
                }

                if (WellKnownSchemas.ContainsKey(id) || !_schemas.TryGetValue(id, out var target))
                {
                    differences.Add($"{where} is {Short(actual)}; the schema is {Short(generated)}");
                    return;
                }

                if (IsEnum(target))
                {
                    CompareEnum(target, shape, where, differences);
                }
                else if (target.OneOf is { Count: > 0 })
                {
                    CompareUnion(id, target, shape, shapes, path, where, differences, visited);
                }
                else
                {
                    CompareObject(id, target, shape, shapes, path, where, differences, visited);
                }

                return;
            }

            if (Has(schema, JsonSchemaType.Array) && schema.Items is { } items)
            {
                if (shape.Kind != "Array" || shape.Element is null)
                {
                    differences.Add($"{where} is {Short(actual)}; the schema is an array");
                    return;
                }

                Compare(items, shape.Element, shapes, path + "[]", differences, visited);
                return;
            }

            if (Canonical(schema) is { } expected && !(shape.Kind == "Primitive" && shape.Primitive == expected))
            {
                differences.Add($"{where} is {Short(actual)}{(shape.Primitive is { } p ? $" ({p})" : string.Empty)}; the schema says {expected}");
            }
        }

        private static void CompareEnum(IOpenApiSchema target, TypeShape shape, string where, List<string> differences)
        {
            if (shape.Kind == "EnumAsNumber")
            {
                differences.Add($"{where} is an enum written as a number; the schema has string values (add [JsonConverter(typeof(JsonStringEnumConverter<{Short(shape.Name)}>))])");
                return;
            }

            if (shape.Kind != "Enum")
            {
                differences.Add($"{where} is {Short(shape.Name)}; the schema is a string enum");
                return;
            }

            var expected = target.Enum!.Select(v => v.GetValue<string>()).ToList();
            foreach (var missing in expected.Except(shape.Values, StringComparer.Ordinal))
            {
                differences.Add($"{where} has no value '{missing}'");
            }

            foreach (var extra in shape.Values.Except(expected, StringComparer.Ordinal))
            {
                differences.Add($"{where} has the value '{extra}', which the schema doesn't");
            }
        }

        private void CompareUnion(string id, IOpenApiSchema target, TypeShape shape, IReadOnlyDictionary<string, TypeShape> shapes, string path, string where,
            List<string> differences, HashSet<string> visited)
        {
            var property = target.Discriminator!.PropertyName!;
            if (shape.Discriminator is null)
            {
                differences.Add($"{where} is {Short(shape.Name)}, which isn't polymorphic; the schema is a oneOf discriminated by '{property}'");
                return;
            }

            if (shape.Discriminator != property)
            {
                differences.Add($"{where} is discriminated by '{shape.Discriminator}'; the schema uses '{property}'");
            }

            var members = _unionMembers.Where(m => m.Value.Base == id).ToList();
            foreach (var member in members)
            {
                var derived = shape.Derived.FirstOrDefault(d => d.Value == member.Value.Value);
                if (derived.Type is null)
                {
                    differences.Add($"{where} has no derived type for '{member.Value.Value}' ({member.Key})");
                    continue;
                }

                Compare(new OpenApiSchemaReference(member.Key, _document), derived.Type, shapes, $"{path}({member.Value.Value})", differences, visited);
            }

            foreach (var extra in shape.Derived.Where(d => members.All(m => m.Value.Value != d.Value)))
            {
                differences.Add($"{where} has a derived type for '{extra.Value}', which the schema doesn't");
            }
        }

        private void CompareObject(string id, IOpenApiSchema target, TypeShape shape, IReadOnlyDictionary<string, TypeShape> shapes, string path, string where,
            List<string> differences, HashSet<string> visited)
        {
            if (shape.Kind != "Object")
            {
                differences.Add($"{where} is {Short(shape.Name)}; the schema is an object");
                return;
            }

            _unionMembers.TryGetValue(id, out var union);
            var properties = target.Properties ?? new Dictionary<string, IOpenApiSchema>();
            foreach (var property in properties.Where(p => p.Key != union.Property))
            {
                var name = path.Length == 0 ? property.Key : $"{path}.{property.Key}";
                var actual = shape.Properties.FirstOrDefault(p => p.Json == property.Key);
                if (actual is null)
                {
                    differences.Add($"{name} is missing");
                    continue;
                }

                Compare(property.Value, actual.Type, shapes, name, differences, visited);
                if (RefId(property.Value) is null)
                {
                    differences.AddRange(Constraints.Differences($"{name} ({actual.Clr})", Constraints.Of(property.Value), actual.Constraints, actual.Misplaced));
                }

                var required = target.Required?.Contains(property.Key) == true;
                var nullable = !required || Has(property.Value, JsonSchemaType.Null);
                if (nullable != actual.Nullable)
                {
                    differences.Add(nullable
                        ? $"{name} ({actual.Clr}) can't be null; the schema {(required ? "allows null" : "makes it optional")}"
                        : $"{name} ({actual.Clr}) can be null; the schema requires it");
                }
            }

            foreach (var extra in shape.Properties.Where(p => !properties.ContainsKey(p.Json) && p.Json != union.Property))
            {
                differences.Add($"{(path.Length == 0 ? extra.Json : $"{path}.{extra.Json}")} ({extra.Clr}) is not in the schema");
            }
        }

        /// <summary>The JSON type and format a primitive schema means, in the same terms as <see cref="TypeShape.Primitive"/>.</summary>
        private static string? Canonical(IOpenApiSchema schema)
        {
            switch ((schema.Type ?? 0) & ~JsonSchemaType.Null)
            {
                case JsonSchemaType.String:
                    return schema.Format is "date-time" or "date" or "uuid" ? $"string/{schema.Format}" : "string";
                case JsonSchemaType.Integer:
                    return schema.Format == "int64" ? "integer/int64" : "integer/int32";
                case JsonSchemaType.Number:
                    return schema.Format == "float" ? "number/float" : "number/double";
                case JsonSchemaType.Boolean:
                    return "boolean";
                default:
                    return null;
            }
        }

        private static string Short(string name) => CoverageVerifier.TypeName(name);
    }
}
