using System.Collections.Generic;
using System.Linq;
using Microsoft.OpenApi;

namespace Matchmark.Generator
{
    /// <summary>
    /// Comparing a C# type's JSON shape with the schema it stands for, from the spec's <see cref="ApiShape"/> alone. The
    /// types generated from the spec match it by construction; any other type is compared property by property, as
    /// System.Text.Json would read and write it.
    /// </summary>
    internal static class ModelCheck
    {
        /// <summary>The type generated from <paramref name="schema"/>, or an array of it, or the primitive it is.</summary>
        public static bool ByConstruction(IReadOnlyDictionary<string, ComponentShape> components, SchemaShape schema, string actual, IReadOnlyDictionary<string, TypeShape> shapes)
        {
            switch (schema.Kind)
            {
                case SchemaShape.RefKind:
                    // A framework type stands for its schema, and so does anything derived from it (ValidationProblemDetails).
                    var generated = Generated(components, schema.Ref!);
                    return actual == generated
                           || (components.TryGetValue(schema.Ref!, out var component) && component.WellKnown && shapes.TryGetValue(actual, out var derived) && derived.Bases.Contains(generated));
                case SchemaShape.ArrayKind:
                    return shapes.TryGetValue(actual, out var shape) && shape.Kind == "Array" && shape.Element is { } element
                           && ByConstruction(components, schema.Items!, element, shapes);
                case SchemaShape.PrimitiveKind:
                    // A primitive isn't generated: it is consistent when it is the primitive the schema says.
                    return shapes.TryGetValue(actual, out var p) && p.Kind == "Primitive" && p.Primitive == schema.Primitive;
                default:
                    return true;
            }
        }

        public static List<string> Differences(IReadOnlyDictionary<string, ComponentShape> components, SchemaShape schema, string actual, IReadOnlyDictionary<string, TypeShape> shapes)
        {
            var differences = new List<string>();
            Compare(components, schema, actual, shapes, string.Empty, differences, new HashSet<string>());
            return differences;
        }

        /// <summary>The full name of the type generated from a component schema, or the framework type it maps to.</summary>
        private static string Generated(IReadOnlyDictionary<string, ComponentShape> components, string id)
            => components.TryGetValue(id, out var component) ? component.Generated : id;

        private static void Compare(IReadOnlyDictionary<string, ComponentShape> components, SchemaShape schema, string actual, IReadOnlyDictionary<string, TypeShape> shapes,
            string path, List<string> differences, HashSet<string> visited)
        {
            var where = path.Length == 0 ? "the type" : path;
            if (!shapes.TryGetValue(actual, out var shape))
            {
                differences.Add($"{where} is {Short(actual)}, which can't be read");
                return;
            }

            switch (schema.Kind)
            {
                case SchemaShape.RefKind:
                    var id = schema.Ref!;
                    var generated = Generated(components, id);
                    if (actual == generated || shape.Bases.Contains(generated) || !visited.Add($"{id}|{actual}"))
                    {
                        return;
                    }

                    if (!components.TryGetValue(id, out var target) || target.WellKnown)
                    {
                        differences.Add($"{where} is {Short(actual)}; the schema is {Short(generated)}");
                        return;
                    }

                    switch (target.Kind)
                    {
                        case ComponentShape.EnumKind:
                            CompareEnum(target, shape, where, differences);
                            break;
                        case ComponentShape.UnionKind:
                            CompareUnion(components, target, shape, shapes, path, where, differences, visited);
                            break;
                        default:
                            CompareObject(components, target, shape, shapes, path, where, differences, visited);
                            break;
                    }

                    return;
                case SchemaShape.ArrayKind:
                    if (shape.Kind != "Array" || shape.Element is null)
                    {
                        differences.Add($"{where} is {Short(actual)}; the schema is an array");
                        return;
                    }

                    Compare(components, schema.Items!, shape.Element, shapes, path + "[]", differences, visited);
                    return;
                case SchemaShape.PrimitiveKind when !(shape.Kind == "Primitive" && shape.Primitive == schema.Primitive):
                    differences.Add($"{where} is {Short(actual)}{(shape.Primitive is { } p ? $" ({p})" : string.Empty)}; the schema says {schema.Primitive}");
                    return;
            }
        }

        private static void CompareEnum(ComponentShape target, TypeShape shape, string where, List<string> differences)
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

            foreach (var missing in target.Values.Except(shape.Values, System.StringComparer.Ordinal))
            {
                differences.Add($"{where} has no value '{missing}'");
            }

            foreach (var extra in shape.Values.Except(target.Values, System.StringComparer.Ordinal))
            {
                differences.Add($"{where} has the value '{extra}', which the schema doesn't");
            }
        }

        private static void CompareUnion(IReadOnlyDictionary<string, ComponentShape> components, ComponentShape target, TypeShape shape, IReadOnlyDictionary<string, TypeShape> shapes,
            string path, string where, List<string> differences, HashSet<string> visited)
        {
            if (target.Discriminator is not { } property)
            {
                differences.Add($"{where} is {Short(shape.Name)}; the schema is a oneOf without a discriminator, which no C# type matches by name");
                return;
            }

            if (shape.Discriminator is null)
            {
                differences.Add($"{where} is {Short(shape.Name)}, which isn't polymorphic; the schema is a oneOf discriminated by '{property}'");
                return;
            }

            if (shape.Discriminator != property)
            {
                differences.Add($"{where} is discriminated by '{shape.Discriminator}'; the schema uses '{property}'");
            }

            foreach (var member in target.Members)
            {
                var derived = shape.Derived.FirstOrDefault(d => d.Value == member.Value);
                if (derived.Type is null)
                {
                    differences.Add($"{where} has no derived type for '{member.Value}' ({member.Id})");
                    continue;
                }

                Compare(components, new SchemaShape(SchemaShape.RefKind, Ref: member.Id), derived.Type, shapes, $"{path}({member.Value})", differences, visited);
            }

            foreach (var extra in shape.Derived.Where(d => target.Members.All(m => m.Value != d.Value)))
            {
                differences.Add($"{where} has a derived type for '{extra.Value}', which the schema doesn't");
            }
        }

        private static void CompareObject(IReadOnlyDictionary<string, ComponentShape> components, ComponentShape target, TypeShape shape, IReadOnlyDictionary<string, TypeShape> shapes,
            string path, string where, List<string> differences, HashSet<string> visited)
        {
            if (shape.Kind != "Object")
            {
                differences.Add($"{where} is {Short(shape.Name)}; the schema is an object");
                return;
            }

            foreach (var property in target.Properties.Where(p => p.Json != target.UnionProperty))
            {
                var name = path.Length == 0 ? property.Json : $"{path}.{property.Json}";
                var actual = shape.Properties.FirstOrDefault(p => p.Json == property.Json);
                if (actual is null)
                {
                    differences.Add($"{name} is missing");
                    continue;
                }

                Compare(components, property.Schema, actual.Type, shapes, name, differences, visited);
                if (property.Schema.Kind != SchemaShape.RefKind)
                {
                    differences.AddRange(Constraints.Differences($"{name} ({actual.Clr})", property.Constraints.Select(c => (c.Keyword, c.Value)).ToList(), actual.Constraints, actual.Misplaced));
                }

                var nullable = !property.Required || property.AllowsNull;
                if (nullable != actual.Nullable)
                {
                    differences.Add(nullable
                        ? $"{name} ({actual.Clr}) can't be null; the schema {(property.Required ? "allows null" : "makes it optional")}"
                        : $"{name} ({actual.Clr}) can be null; the schema requires it");
                }
            }

            foreach (var extra in shape.Properties.Where(p => target.Properties.All(t => t.Json != p.Json) && p.Json != target.UnionProperty))
            {
                differences.Add($"{(path.Length == 0 ? extra.Json : $"{path}.{extra.Json}")} ({extra.Clr}) is not in the schema");
            }
        }

        private static string Short(string name) => CoverageVerifier.TypeName(name);
    }

    internal sealed partial class Emitter
    {
        /// <summary>The full name of the type generated from a component schema, or the framework type it maps to.</summary>
        private string Generated(string id)
            => WellKnownSchemas.TryGetValue(id, out var wellKnown) ? wellKnown.Replace("global::", string.Empty) : $"{_namespace}.{Naming.Pascal(id)}";

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
    }
}
