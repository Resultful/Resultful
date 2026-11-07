using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Matchmark.Generator
{
    /// <summary>One JSON property of a type, as System.Text.Json (web defaults) would read and write it.</summary>
    internal sealed class ShapeProperty : IEquatable<ShapeProperty>
    {
        public ShapeProperty(string json, string clr, string type, bool nullable, bool required, ImmutableArray<(string Keyword, string Value)> constraints,
            ImmutableArray<(string Keyword, string Value)> misplaced)
        {
            Constraints = constraints;
            Misplaced = misplaced;
            Json = json;
            Clr = clr;
            Type = type;
            Nullable = nullable;
            Required = required;
        }

        public string Json { get; }

        public string Clr { get; }

        /// <summary>The property type's full name, without nullability.</summary>
        public string Type { get; }

        public bool Nullable { get; }

        /// <summary>Declared <c>required</c>, or [JsonRequired].</summary>
        public bool Required { get; }

        /// <summary>Its DataAnnotations, as schema keywords.</summary>
        public ImmutableArray<(string Keyword, string Value)> Constraints { get; }

        /// <summary>
        /// DataAnnotations on the positional record parameter the property comes from. They apply to the parameter, not
        /// the property, so ASP.NET Core's OpenAPI document doesn't carry them; <c>[property: ...]</c> fixes it.
        /// </summary>
        public ImmutableArray<(string Keyword, string Value)> Misplaced { get; }

        public bool Equals(ShapeProperty? other)
            => other is not null && Json == other.Json && Clr == other.Clr && Type == other.Type && Nullable == other.Nullable && Required == other.Required
               && Constraints.SequenceEqual(other.Constraints) && Misplaced.SequenceEqual(other.Misplaced);

        public override bool Equals(object? obj) => Equals(obj as ShapeProperty);

        public override int GetHashCode() => (Json, Type).GetHashCode();
    }

    /// <summary>
    /// The JSON shape of a C# type, for comparing with a schema: a primitive (its JSON type and format), an enum and its
    /// values, an array and its element, an object and its properties, or a polymorphic base and its derived types.
    /// </summary>
    internal sealed class TypeShape : IEquatable<TypeShape>
    {
        public TypeShape(string name, string kind, string? primitive = null, string? element = null,
            ImmutableArray<ShapeProperty>? properties = null, ImmutableArray<string>? values = null, string? discriminator = null,
            ImmutableArray<(string Value, string Type)>? derived = null, ImmutableArray<string>? bases = null)
        {
            Name = name;
            Kind = kind;
            Primitive = primitive;
            Element = element;
            Properties = properties ?? ImmutableArray<ShapeProperty>.Empty;
            Values = values ?? ImmutableArray<string>.Empty;
            Discriminator = discriminator;
            Derived = derived ?? ImmutableArray<(string, string)>.Empty;
            Bases = bases ?? ImmutableArray<string>.Empty;
        }

        public string Name { get; }

        /// <summary>Primitive, Enum, EnumAsNumber, Array, Object or Unknown.</summary>
        public string Kind { get; }

        /// <summary>For a primitive: "integer/int64", "string/uuid", "boolean" and so on.</summary>
        public string? Primitive { get; }

        public string? Element { get; }

        public ImmutableArray<ShapeProperty> Properties { get; }

        public ImmutableArray<string> Values { get; }

        public string? Discriminator { get; }

        public ImmutableArray<(string Value, string Type)> Derived { get; }

        /// <summary>Its base types, so a type derived from a framework type (ValidationProblemDetails) is recognised.</summary>
        public ImmutableArray<string> Bases { get; }

        public bool Equals(TypeShape? other)
            => other is not null && Name == other.Name && Kind == other.Kind && Primitive == other.Primitive && Element == other.Element
               && Discriminator == other.Discriminator && Properties.SequenceEqual(other.Properties) && Values.SequenceEqual(other.Values)
               && Derived.SequenceEqual(other.Derived) && Bases.SequenceEqual(other.Bases);

        public override bool Equals(object? obj) => Equals(obj as TypeShape);

        public override int GetHashCode() => (Name, Kind).GetHashCode();

        /// <summary>The type's full name, without nullability or "global::".</summary>
        public static string NameOf(ITypeSymbol type)
            => Underlying(type).WithNullableAnnotation(NullableAnnotation.NotAnnotated)
                .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty);

        public static ITypeSymbol Underlying(ITypeSymbol type)
            => type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable ? nullable.TypeArguments[0] : type;

        public static bool IsNullable(ITypeSymbol type)
            => !ReferenceEquals(Underlying(type), type) || type.NullableAnnotation == NullableAnnotation.Annotated;

        /// <summary>The shapes of <paramref name="root"/> and every type reachable from it, by name.</summary>
        public static void Collect(ITypeSymbol root, Dictionary<string, TypeShape> into, int depth = 0)
        {
            var type = Underlying(root);
            var name = NameOf(type);
            if (into.ContainsKey(name) || depth > 12)
            {
                return;
            }

            if (PrimitiveOf(type) is { } primitive)
            {
                into[name] = new TypeShape(name, "Primitive", primitive: primitive);
                return;
            }

            if (type is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType)
            {
                var asString = HasStringEnumConverter(enumType);
                var values = enumType.GetMembers().OfType<IFieldSymbol>().Where(f => f.HasConstantValue)
                    .Select(f => f.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "JsonStringEnumMemberNameAttribute")?.ConstructorArguments.FirstOrDefault().Value as string ?? f.Name)
                    .ToImmutableArray();
                into[name] = new TypeShape(name, asString ? "Enum" : "EnumAsNumber", values: values);
                return;
            }

            if (ElementOf(type) is { } element)
            {
                into[name] = new TypeShape(name, "Array", element: NameOf(element));
                Collect(element, into, depth + 1);
                return;
            }

            if (type is not INamedTypeSymbol named || named.TypeKind is not (TypeKind.Class or TypeKind.Struct))
            {
                into[name] = new TypeShape(name, "Unknown");
                return;
            }

            var bases = new List<string>();
            for (var b = named.BaseType; b is not null && b.SpecialType != SpecialType.System_Object; b = b.BaseType)
            {
                bases.Add(NameOf(b));
            }

            var properties = new List<ShapeProperty>();
            var seen = new HashSet<string>();
            for (var current = named; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
            {
                foreach (var property in current.GetMembers().OfType<IPropertySymbol>())
                {
                    if (property.IsStatic || property.IsIndexer || property.DeclaredAccessibility != Accessibility.Public || property.GetMethod is null
                        || property.Name == "EqualityContract" || !seen.Add(property.Name)
                        || property.GetAttributes().Any(a => a.AttributeClass?.Name == "JsonIgnoreAttribute"))
                    {
                        continue;
                    }

                    var json = property.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "JsonPropertyNameAttribute")?.ConstructorArguments.FirstOrDefault().Value as string
                        ?? char.ToLowerInvariant(property.Name[0]) + property.Name.Substring(1);
                    var required = property.IsRequired || property.GetAttributes().Any(a => a.AttributeClass?.Name == "JsonRequiredAttribute");
                    var array = ElementOf(Underlying(property.Type)) is not null;
                    properties.Add(new ShapeProperty(json, property.Name, NameOf(property.Type), IsNullable(property.Type), required,
                        Generator.Constraints.Of(property.GetAttributes(), array),
                        PositionalParameter(property) is { } parameter ? Generator.Constraints.Of(parameter.GetAttributes(), array) : ImmutableArray<(string, string)>.Empty));
                    Collect(property.Type, into, depth + 1);
                }
            }

            var polymorphic = named.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "JsonPolymorphicAttribute");
            var discriminator = polymorphic is null
                ? null
                : polymorphic.NamedArguments.FirstOrDefault(n => n.Key == "TypeDiscriminatorPropertyName").Value.Value as string ?? "$type";
            var derived = named.GetAttributes().Where(a => a.AttributeClass?.Name == "JsonDerivedTypeAttribute")
                .Select(a => (Value: a.ConstructorArguments.Length > 1 ? a.ConstructorArguments[1].Value?.ToString() ?? string.Empty : string.Empty,
                              Type: a.ConstructorArguments[0].Value as ITypeSymbol))
                .Where(d => d.Type is not null)
                .ToList();

            into[name] = new TypeShape(name, "Object", properties: properties.ToImmutableArray(), discriminator: discriminator,
                derived: derived.Select(d => (d.Value, NameOf(d.Type!))).ToImmutableArray(), bases: bases.ToImmutableArray());
            foreach (var d in derived)
            {
                Collect(d.Type!, into, depth + 1);
            }
        }

        /// <summary>The primary constructor parameter a positional record's property is declared by, if it is one.</summary>
        private static IParameterSymbol? PositionalParameter(IPropertySymbol property)
            => property.DeclaringSyntaxReferences.Any(r => r.GetSyntax().IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.Parameter))
                ? property.ContainingType.InstanceConstructors.SelectMany(c => c.Parameters).FirstOrDefault(p => p.Name == property.Name)
                : null;

        private static bool HasStringEnumConverter(INamedTypeSymbol type)
            => type.GetAttributes().Any(a => a.AttributeClass?.Name == "JsonConverterAttribute"
                && a.ConstructorArguments.FirstOrDefault().Value is ITypeSymbol converter && converter.Name.StartsWith("JsonStringEnumConverter", StringComparison.Ordinal));

        private static ITypeSymbol? ElementOf(ITypeSymbol type)
        {
            if (type is IArrayTypeSymbol array)
            {
                return array.ElementType;
            }

            if (type.SpecialType == SpecialType.System_String)
            {
                return null;
            }

            var enumerable = type is INamedTypeSymbol { Name: "IEnumerable", IsGenericType: true } self ? self
                : type.AllInterfaces.FirstOrDefault(i => i.Name == "IEnumerable" && i.IsGenericType && i.ContainingNamespace.ToDisplayString() == "System.Collections.Generic");
            return enumerable?.TypeArguments[0];
        }

        /// <summary>The JSON type and format a C# primitive is written as; null for anything else.</summary>
        public static string? PrimitiveOf(ITypeSymbol type) => type.SpecialType switch
        {
            SpecialType.System_String => "string",
            SpecialType.System_Boolean => "boolean",
            SpecialType.System_Int32 or SpecialType.System_Int16 or SpecialType.System_Byte or SpecialType.System_SByte or SpecialType.System_UInt16 => "integer/int32",
            SpecialType.System_Int64 or SpecialType.System_UInt32 => "integer/int64",
            SpecialType.System_Double => "number/double",
            SpecialType.System_Single => "number/float",
            SpecialType.System_Decimal => "number/double", // ASP.NET Core's OpenAPI document describes decimal as a double
            SpecialType.System_DateTime => "string/date-time",
            _ => type.ToDisplayString() switch
            {
                "System.DateTimeOffset" => "string/date-time",
                "System.DateOnly" => "string/date",
                "System.Guid" => "string/uuid",
                _ => null,
            },
        };
    }
}
