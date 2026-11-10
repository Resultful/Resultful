using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Matchmark.Generator
{
    /// <summary>
    /// An array compared by its items, so a shape holding one is equal to another with the same items: what Roslyn's
    /// incremental pipeline needs to skip a step whose input didn't change (model.md, its shape in the pipeline).
    /// </summary>
    internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T> where T : IEquatable<T>
    {
        private readonly ImmutableArray<T> _items;

        public EquatableArray(IEnumerable<T> items) => _items = items.ToImmutableArray();

        public static EquatableArray<T> Empty => new(Array.Empty<T>());

        public int Count => _items.IsDefault ? 0 : _items.Length;

        public bool Equals(EquatableArray<T> other) => this.SequenceEqual(other);

        public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

        public override int GetHashCode()
        {
            var hash = 17;
            foreach (var item in this)
            {
                hash = unchecked((hash * 31) + (item?.GetHashCode() ?? 0));
            }

            return hash;
        }

        public IEnumerator<T> GetEnumerator() => (_items.IsDefault ? ImmutableArray<T>.Empty : _items).AsEnumerable().GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    internal static class EquatableArray
    {
        public static EquatableArray<T> ToEquatable<T>(this IEnumerable<T> items) where T : IEquatable<T> => new(items);
    }

    /// <summary>Where a diagnostic goes, as data: a Location holds its syntax tree, so it's never equal across edits.</summary>
    internal sealed record LocationInfo(string FilePath, TextSpan Span, LinePositionSpan Lines)
    {
        public static LocationInfo From(Location location)
            => new(location.SourceTree?.FilePath ?? location.GetLineSpan().Path, location.SourceSpan, location.GetLineSpan().Span);

        public Location ToLocation() => Location.Create(FilePath, Span, Lines);
    }

    /// <summary>A validation keyword and its value as text (<c>maxLength</c>, <c>50</c>).</summary>
    internal sealed record ConstraintShape(string Keyword, string Value);

    /// <summary>
    /// How a schema is used where it appears: a reference to a component, an array of something, a primitive (its JSON type
    /// and format, as <c>TypeShape.Primitive</c> names them), or anything else.
    /// </summary>
    internal sealed record SchemaShape(string Kind, string? Ref = null, string? Primitive = null, SchemaShape? Items = null)
    {
        public const string RefKind = "Ref";
        public const string ArrayKind = "Array";
        public const string PrimitiveKind = "Primitive";
        public const string OtherKind = "Other";

        /// <summary>"Pet", "Pet[]", "string": how the schema is named in messages.</summary>
        public string Name => Kind switch
        {
            RefKind => Ref!,
            ArrayKind => Items!.Name + "[]",
            PrimitiveKind => Primitive!,
            _ => "object",
        };
    }

    /// <summary>One property of an object component: its JSON name, its schema, and what the schema says of it.</summary>
    internal sealed record PropertyShape(string Json, SchemaShape Schema, EquatableArray<ConstraintShape> Constraints, bool Required, bool AllowsNull);

    /// <summary>A member of a union, and the discriminator value that selects it.</summary>
    internal sealed record UnionMemberShape(string Id, string Value);

    /// <summary>
    /// A component schema, decided: the C# type generated for it (or the framework type it maps to), and what it is: a
    /// string enum and its values, a discriminated union and its members, or an object and its properties.
    /// </summary>
    internal sealed record ComponentShape(
        string Id,
        string Generated,
        bool WellKnown,
        string Kind,
        EquatableArray<string> Values,
        string? Discriminator,
        EquatableArray<UnionMemberShape> Members,
        EquatableArray<PropertyShape> Properties,
        string? UnionProperty)
    {
        public const string EnumKind = "Enum";
        public const string UnionKind = "Union";
        public const string ObjectKind = "Object";
    }

    /// <summary>A path, query or header parameter: where it binds from, its wire name, its C# type, and its constraints.</summary>
    internal sealed record ParameterShape(string Source, string Name, string Type, bool Required, EquatableArray<ConstraintShape> Constraints);

    /// <summary>A documented response: its status, and its body's schema (null when it has no body).</summary>
    internal sealed record ResponseShape(int Status, SchemaShape? Schema);

    /// <summary>An operation, decided: what an endpoint must bind and answer to implement it.</summary>
    internal sealed record OperationShape(
        string Id,
        string Method,
        string Path,
        string Tag,
        EquatableArray<ParameterShape> Parameters,
        string? BodyType,
        SchemaShape? Body,
        EquatableArray<ResponseShape> Responses)
    {
        public IEnumerable<int> Statuses => Responses.Select(r => r.Status).OrderBy(s => s);
    }

    /// <summary>
    /// A spec, parsed once: its operations and components, everything decided and nothing from Microsoft.OpenApi in it,
    /// so the steps that read it are reused when an edit leaves it equal.
    /// </summary>
    internal sealed record ApiShape(string ApiName, string Namespace, string Path, EquatableArray<OperationShape> Operations, EquatableArray<ComponentShape> Components);
}
