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
    /// <summary>The emitter's schemas (decision Q10: the model and its outputs, split by output).</summary>
    internal sealed partial class Emitter
    {
        // ---------------------------------------------------------------- schemas

        private void CollectSchemas()
        {
            var components = _document.Components?.Schemas;
            if (components is null || _collected)
            {
                return;
            }

            _collected = true;

            foreach (var pair in components)
            {
                if (Mapped.ContainsKey(pair.Key))
                {
                    continue;
                }

                var schema = pair.Value;
                if (IsUnion(schema))
                {
                    if (!TryCollectUnion(pair.Key, schema))
                    {
                        continue;
                    }
                }
                else if (schema.AllOf is { Count: > 0 })
                {
                    if (Flatten(schema) is not { } flat)
                    {
                        Unsupported($"Schema '{pair.Key}'", "allOf of anything but objects");
                        continue;
                    }

                    schema = flat;
                }
                else if (!IsEnum(schema) && !IsObject(schema))
                {
                    if (IsAlias(schema))
                    {
                        _aliases[pair.Key] = schema;
                        continue;
                    }

                    Unsupported($"Schema '{pair.Key}'", "only objects, string enums, discriminated oneOf, allOf, primitives and arrays are generated");
                    continue;
                }

                _schemas[pair.Key] = schema;
            }

            // Inline objects in the component schemas' properties get types of their own, named from where they are.
            foreach (var pair in _schemas.ToList())
            {
                RegisterInline(Naming.Pascal(pair.Key), pair.Value);
            }
        }

        /// <summary>A primitive or an array (of anything the generator can type): used where it's referred to, not generated.</summary>
        private static bool IsAlias(IOpenApiSchema schema)
        {
            var type = (schema.Type ?? 0) & ~JsonSchemaType.Null;
            return type is JsonSchemaType.String or JsonSchemaType.Integer or JsonSchemaType.Number or JsonSchemaType.Boolean
                   || type == JsonSchemaType.Array && schema.Items is not null
                   // Any JSON value: an object with no properties, no type at all, or an anyOf nothing tells apart. Each is
                   // JsonElement where it's used, so what refers to it isn't skipped.
                   || type == JsonSchemaType.Object && schema.Properties is not { Count: > 0 }
                   || type == 0 && schema.Properties is not { Count: > 0 } && schema.AllOf is not { Count: > 0 }
                   || schema.AnyOf is { Count: > 0 } && schema.Properties is not { Count: > 0 };
        }

        /// <summary>An allOf as one object: every member's properties and required, members' own allOfs included.</summary>
        private static OpenApiSchema? Flatten(IOpenApiSchema schema, int depth = 0)
        {
            var flat = new OpenApiSchema
            {
                Type = JsonSchemaType.Object,
                Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal),
                Required = new HashSet<string>(StringComparer.Ordinal),
                Description = schema.Description,
            };
            foreach (var part in (schema.AllOf ?? new List<IOpenApiSchema>()).Append(schema))
            {
                if (ReferenceEquals(part, schema))
                {
                    Merge(flat, part.Properties, part.Required);
                    continue;
                }

                if (part.AllOf is { Count: > 0 })
                {
                    if (depth > 8 || Flatten(part, depth + 1) is not { } inner)
                    {
                        return null;
                    }

                    Merge(flat, inner.Properties, inner.Required);
                }
                else if (part.Properties is { Count: > 0 } || Has(part, JsonSchemaType.Object))
                {
                    Merge(flat, part.Properties, part.Required);
                }
                else
                {
                    return null;
                }
            }

            return flat;
        }

        private static void Merge(OpenApiSchema flat, IDictionary<string, IOpenApiSchema>? properties, ISet<string>? required)
        {
            foreach (var property in properties ?? new Dictionary<string, IOpenApiSchema>())
            {
                flat.Properties![property.Key] = property.Value;
            }

            foreach (var name in required ?? new HashSet<string>())
            {
                flat.Required!.Add(name);
            }
        }

        /// <summary>Registers, under names from where they are, the inline objects in a schema's properties (and their items).</summary>
        private void RegisterInline(string name, IOpenApiSchema schema)
        {
            foreach (var property in (schema.Properties ?? new Dictionary<string, IOpenApiSchema>()).ToList())
            {
                var target = property.Value.Items is { } items && Has(property.Value, JsonSchemaType.Array) ? items : property.Value;
                var suffix = ReferenceEquals(target, property.Value) ? string.Empty : "Item";
                if (RefId(target) is null && IsInline(target))
                {
                    Inline($"{name}{Naming.Pascal(property.Key)}{suffix}", target);
                }
            }
        }

        private static bool IsInline(IOpenApiSchema schema) => schema.AllOf is { Count: > 0 } || IsObject(schema);

        /// <summary>The type generated for an inline object or allOf, named <paramref name="name"/> (or that with a number) the first time.</summary>
        private string? Inline(string name, IOpenApiSchema schema)
        {
            if (_inline.TryGetValue(schema, out var known))
            {
                return known;
            }

            var shape = schema.AllOf is { Count: > 0 } ? Flatten(schema) : schema;
            if (shape is null)
            {
                return null;
            }

            var unique = name;
            for (var n = 2; _schemas.ContainsKey(unique) || _aliases.ContainsKey(unique) || WellKnownSchemas.ContainsKey(unique); n++)
            {
                unique = $"{name}{n}";
            }

            _inline[schema] = unique;
            _schemas[unique] = shape;
            RegisterInline(unique, shape);
            return unique;
        }

        /// <summary>Schemas compared by identity: the same inline object is the same type, however often it's seen.</summary>
        private sealed class Identity : IEqualityComparer<IOpenApiSchema>
        {
            public static readonly Identity Instance = new();

            public bool Equals(IOpenApiSchema? x, IOpenApiSchema? y) => ReferenceEquals(x, y);

            public int GetHashCode(IOpenApiSchema obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }

        private bool TryCollectUnion(string name, IOpenApiSchema schema)
        {
            var property = schema.Discriminator?.PropertyName;
            if (string.IsNullOrEmpty(property))
            {
                // No discriminator, so no union type can be told apart: typed as JSON where it's used (MM009).
                _aliases[name] = schema;
                return false;
            }

            var mapping = new Dictionary<string, string>(StringComparer.Ordinal);
            if (schema.Discriminator!.Mapping is { } explicitMapping)
            {
                foreach (var entry in explicitMapping)
                {
                    mapping[entry.Value.Reference.Id!] = entry.Key;
                }
            }

            foreach (var member in schema.OneOf!)
            {
                if (member is not OpenApiSchemaReference reference || reference.Reference.Id is not { } id)
                {
                    Unsupported($"Schema '{name}'", "oneOf members must be $refs to component schemas");
                    return false;
                }

                _unionMembers[id] = (name, property!, mapping.TryGetValue(id, out var value) ? value : id);
            }

            return true;
        }

        private static bool IsEnum(IOpenApiSchema schema)
            => schema.Enum is { Count: > 0 } && Has(schema, JsonSchemaType.String);

        /// <summary>
        /// A union the generator writes as a record hierarchy: a oneOf with a discriminator. A oneOf beside an object's
        /// properties without one (GitHub: "one of these fields is required") only constrains the object, and it is the
        /// object that's generated.
        /// </summary>
        /// <summary>
        /// The C# name of an object's property: Pascal case; a leading + or - before a digit spelled out (GitHub's
        /// reactions "+1" and "-1" are Plus1 and Minus1); "Value" added to a name its type has (Email.email); and a
        /// number added to a later property whose name would repeat an earlier one's. The JSON name is kept by
        /// [JsonPropertyName].
        /// </summary>
        internal static string Member(string type, IOpenApiSchema owner, string key)
        {
            static string Basic(string type, string key)
            {
                var spelled = key.Length > 1 && char.IsDigit(key[1]) && key[0] is '+' or '-' ? (key[0] == '+' ? "Plus" : "Minus") + key.Substring(1) : key;
                var name = Naming.Pascal(spelled);
                return name == type ? name + "Value" : name;
            }

            var basic = Basic(type, key);
            var earlier = (owner.Properties ?? new Dictionary<string, IOpenApiSchema>()).Keys.TakeWhile(k => k != key).Count(k => Basic(type, k) == basic);
            return earlier == 0 ? basic : basic + (earlier + 1).ToString(CultureInfo.InvariantCulture);
        }

        private static bool IsUnion(IOpenApiSchema schema)
            => schema.OneOf is { Count: > 0 } && !(schema.Discriminator?.PropertyName is null && IsObject(schema));

        private static bool IsObject(IOpenApiSchema schema)
            => (schema.Type is null || Has(schema, JsonSchemaType.Object)) && schema.Properties is { Count: > 0 };

        private static bool Has(IOpenApiSchema schema, JsonSchemaType type)
            => schema.Type is { } t && (t & type) == type;

        private static string? RefId(IOpenApiSchema schema)
            => schema is OpenApiSchemaReference reference ? reference.Reference.Id : null;

        /// <summary>The C# type for a schema used as a property, parameter or body.</summary>
        /// <param name="hint">The name to give an inline object here, if it is one (a request or response body).</param>
        private string? TypeOf(IOpenApiSchema schema, string context, string? hint = null, int depth = 0)
        {
            if (RefId(schema) is { } id)
            {
                if (Mapped.TryGetValue(id, out var wellKnown))
                {
                    return wellKnown;
                }

                if (_schemas.ContainsKey(id))
                {
                    return Naming.Pascal(id);
                }

                if (_aliases.TryGetValue(id, out var alias) && depth < 8)
                {
                    return TypeOf(alias, context, hint, depth + 1);
                }

                Unsupported(context, $"$ref to skipped or missing schema '{id}'");
                return null;
            }

            if (IsInline(schema) && (_inline.TryGetValue(schema, out var named) ? named : hint is null ? null : Inline(hint, schema)) is { } inline)
            {
                return Naming.Pascal(inline);
            }

            // A oneOf or anyOf the serializer can't tell apart (no discriminator): its value, as JSON.
            if (schema.OneOf is { Count: > 0 } || schema.AnyOf is { Count: > 0 })
            {
                if (_loose.Add(schema))
                {
                    Diagnostics.Add(Diagnostic.Create(Generator.Diagnostics.LooselyTyped, _location, context, "a oneOf or anyOf without a discriminator"));
                }

                return "global::System.Text.Json.JsonElement";
            }

            // No type, or an object with no properties: any JSON value, which is exactly what JsonElement is.
            if ((schema.Type ?? 0) == 0 && schema.Properties is not { Count: > 0 } && schema.AllOf is not { Count: > 0 }
                || (schema.Type ?? 0) == JsonSchemaType.Object && schema.Properties is not { Count: > 0 } && schema.AdditionalProperties is null)
            {
                return "global::System.Text.Json.JsonElement";
            }

            var type = (schema.Type ?? 0) & ~JsonSchemaType.Null;
            switch (type)
            {
                case JsonSchemaType.String:
                    return schema.Format switch
                    {
                        "date-time" => "global::System.DateTimeOffset",
                        "date" => "global::System.DateOnly",
                        "uuid" => "global::System.Guid",
                        _ => "string",
                    };
                case JsonSchemaType.Integer:
                    return schema.Format == "int64" ? "long" : "int";
                case JsonSchemaType.Number:
                    return schema.Format switch
                    {
                        "float" => "float",
                        "decimal" => "decimal",
                        _ => "double",
                    };
                case JsonSchemaType.Boolean:
                    return "bool";
                case JsonSchemaType.Array when schema.Items is not null:
                    return TypeOf(schema.Items, context, hint is null ? null : hint + "Item", depth + 1) is { } item
                        ? $"global::System.Collections.Generic.IReadOnlyList<{item}>"
                        : null;
                case JsonSchemaType.Object when schema.AdditionalProperties is not null && schema.Properties is not { Count: > 0 }:
                    return TypeOf(schema.AdditionalProperties, context) is { } value
                        ? $"global::System.Collections.Generic.IReadOnlyDictionary<string, {value}>"
                        : null;
                default:
                    Unsupported(context, "inline object, untyped or multi-typed schemas; use a component $ref");
                    return null;
            }
        }

        private static bool IsValueType(string type)
            => type is "int" or "long" or "float" or "double" or "decimal" or "bool" or "global::System.Text.Json.JsonElement"
               || type.StartsWith("global::System.DateTimeOffset", StringComparison.Ordinal)
               || type.StartsWith("global::System.DateOnly", StringComparison.Ordinal)
               || type.StartsWith("global::System.Guid", StringComparison.Ordinal);

        private bool IsEnumRef(IOpenApiSchema schema)
            => RefId(schema) is { } id && _schemas.TryGetValue(id, out var target) && IsEnum(target);

        private string EmitModels()
        {
            var w = new CodeWriter(_namespace);
            w.Usings("System.Text.Json.Serialization");

            if (_schemas.Values.Any(IsEnum))
            {
                // The spec's enums are strings: a number is refused, where JsonStringEnumConverter would take it as a member.
                w.Line("/// <summary>Reads and writes an enum by its names in the spec, and refuses a number.</summary>");
                w.Open($"public sealed class {_apiName}EnumConverter<T> : JsonStringEnumConverter<T> where T : struct, global::System.Enum");
                w.Line($"public {_apiName}EnumConverter() : base(null, allowIntegerValues: false) {{ }}");
                w.Close();
                w.Line();
            }

            foreach (var pair in _schemas)
            {
                var name = Naming.Pascal(pair.Key);
                var schema = pair.Value;

                if (IsEnum(schema))
                {
                    w.Line($"[JsonConverter(typeof({_apiName}EnumConverter<{name}>))]");
                    w.Open($"public enum {name}");
                    foreach (var value in schema.Enum!.Select(v => v.GetValue<string>()))
                    {
                        w.Line($"[JsonStringEnumMemberName({Naming.Literal(value)})]");
                        w.Line($"{Naming.Pascal(value)},");
                    }

                    w.Close();
                    w.Line();
                    continue;
                }

                if (IsUnion(schema))
                {
                    w.Line($"[JsonPolymorphic(TypeDiscriminatorPropertyName = {Naming.Literal(schema.Discriminator!.PropertyName!)})]");
                    foreach (var member in _unionMembers.Where(m => m.Value.Base == pair.Key))
                    {
                        w.Line($"[JsonDerivedType(typeof({Naming.Pascal(member.Key)}), {Naming.Literal(member.Value.Value)})]");
                    }

                    w.Line($"public abstract record {name};");
                    w.Line();
                    continue;
                }

                var hasBase = _unionMembers.TryGetValue(pair.Key, out var union);
                if (schema.AdditionalPropertiesAllowed == false || schema.AdditionalProperties is { Not: not null })
                {
                    // additionalProperties: false: a property the schema doesn't name is refused when read.
                    w.Line("[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]");
                }

                // Rules across fields (x-matchmark-rules) make the model an IValidatableObject, for .NET's validation layer.
                var rules = _hasRules ? Rules(schema).ToList() : new List<(string Rule, string? Message)>();
                var validatable = rules.Count > 0 ? "global::System.ComponentModel.DataAnnotations.IValidatableObject" : null;
                w.Open(hasBase ? $"public sealed record {name} : {Naming.Pascal(union.Base)}{(validatable is null ? string.Empty : ", " + validatable)}"
                    : $"public sealed record {name}{(validatable is null ? string.Empty : " : " + validatable)}");
                if (validatable is not null)
                {
                    w.Line("/// <summary>The spec's rules across this model's fields (x-matchmark-rules), for .NET's validation layer: each broken one, keyed at the model.</summary>");
                    w.Line($"global::System.Collections.Generic.IEnumerable<global::System.ComponentModel.DataAnnotations.ValidationResult> {validatable}.Validate(global::System.ComponentModel.DataAnnotations.ValidationContext validationContext)");
                    w.Line($"    => {ValidationClass}.Broken(this, {string.Join(", ", rules.Select(r => $"({Naming.Literal(r.Rule)}, {Naming.Literal(r.Message ?? $"Must satisfy {r.Rule}.")})"))});");
                    w.Line();
                }

                foreach (var property in schema.Properties!)
                {
                    // The discriminator is written and read by System.Text.Json from the base type's attributes.
                    if (hasBase && property.Key == union.Property)
                    {
                        continue;
                    }

                    var type = TypeOf(property.Value, $"Property '{pair.Key}.{property.Key}'");
                    if (type is null)
                    {
                        continue;
                    }

                    var required = schema.Required?.Contains(property.Key) == true;
                    var nullable = !required || Has(property.Value, JsonSchemaType.Null);
                    w.Line($"[JsonPropertyName({Naming.Literal(property.Key)})]");
                    if (!required && !Has(property.Value, JsonSchemaType.Null))
                    {
                        // Optional, but never null: absent is left out, not written as null (and null is refused when read).
                        w.Line("[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]");
                    }
                    foreach (var annotation in Annotations(property.Value, type, nullable, ValidationAttributes))
                    {
                        w.Line(annotation);
                    }

                    w.Line($"public {(required ? "required " : string.Empty)}{type}{(nullable ? "?" : string.Empty)} {Member(name, schema, property.Key)} {{ get; init; }}");
                    _modelProperties.Add((name, Member(name, schema, property.Key), $"{type}{(nullable ? "?" : string.Empty)}", property.Key, ResolverAttributes(property.Value, type, nullable).ToList()));
                    if (NetValidation && !_shared)
                    {
                        foreach (var keyword in Unchecked(property.Value))
                        {
                            Diagnostics.Add(Diagnostic.Create(Generator.Diagnostics.KeywordUnchecked, _location, $"{pair.Key}.{property.Key}", keyword));
                        }
                    }
                    w.Line();
                }

                w.TrimBlankLine();
                w.Close();
                w.Line();
            }

            return w.ToString();
        }

        /// <summary>
        /// DataAnnotations mirroring the constraints (validation.md, the attribute table). They are not yet what validates
        /// (the generated validator is); they let ASP.NET Core's own OpenAPI document carry the constraints, so the
        /// round-trip check can compare them, and .NET's validation layer read them. A required member needs no
        /// [Required]: it's a C# required member, and System.Text.Json refuses a body without it. A one-sided bound has
        /// no attribute (Range needs both), so it's checked by the generated validator only.
        /// </summary>
        private static IEnumerable<string> Annotations(IOpenApiSchema schema, string type, bool nullable = false, bool extras = false)
            => AnnotationParts(schema, type, nullable, extras).Select(a => $"[{(a.Name.StartsWith("global::", StringComparison.Ordinal) ? string.Empty : "global::System.ComponentModel.DataAnnotations.")}{a.Name}{(a.Args.Length + a.Named.Length == 0 ? string.Empty : $"({string.Join(", ", new[] { a.Args, a.Named }.Where(x => x.Length > 0))})")}]");

        /// <summary>The attributes, as their names, positional arguments and named ones, so the resolver can construct them too.</summary>
        private static IEnumerable<(string Name, string Args, string Named)> AnnotationParts(IOpenApiSchema schema, string type, bool nullable, bool extras)
        {
            // What .NET has no attribute for, from Matchmark.Validation when the project references it.
            if (extras && schema.MultipleOf is { } step)
            {
                yield return ("global::Matchmark.Validation.MultipleOf", Double(step.ToString(CultureInfo.InvariantCulture)), string.Empty);
            }

            if (extras && schema.UniqueItems == true)
            {
                yield return ("global::Matchmark.Validation.UniqueItems", string.Empty, string.Empty);
            }

            if (extras && (schema.MinProperties is not null || schema.MaxProperties is not null))
            {
                yield return ("global::Matchmark.Validation.PropertyCount", $"{(schema.MinProperties ?? -1).Invariant()}, {(schema.MaxProperties ?? -1).Invariant()}", string.Empty);
            }

            var array = (schema.Type ?? 0).HasFlag(JsonSchemaType.Array);
            if ((array ? schema.MinItems : schema.MinLength) is { } min)
            {
                yield return ("MinLength", min.Invariant(), string.Empty);
            }

            if ((array ? schema.MaxItems : schema.MaxLength) is { } max)
            {
                yield return ("MaxLength", max.Invariant(), string.Empty);
            }

            if (schema.Pattern is { } pattern)
            {
                yield return ("RegularExpression", Naming.Literal(pattern), string.Empty);
            }

            var lower = schema.ExclusiveMinimum ?? schema.Minimum;
            var upper = schema.ExclusiveMaximum ?? schema.Maximum;
            if (lower is not null && upper is not null)
            {
                var exclusive = new List<string>();
                if (schema.ExclusiveMinimum is not null)
                {
                    exclusive.Add("MinimumIsExclusive = true");
                }

                if (schema.ExclusiveMaximum is not null)
                {
                    exclusive.Add("MaximumIsExclusive = true");
                }

                // Doubles, so an integer bound and a fractional one make the same constructor.
                yield return ("Range", $"{Double(lower)}, {Double(upper)}", string.Join(", ", exclusive));
            }

            if (type != "string")
            {
                yield break;
            }

            // A string enum the model keeps as text (an enum schema of its own is a C# enum, which needs none), or a const.
            var values = Allowed(schema);
            if (values.Count > 0)
            {
                // An optional or nullable value may be absent: AllowedValues refuses null unless it's listed.
                yield return ("AllowedValues", string.Join(", ", values.Select(Naming.Literal).Concat(nullable ? NullValue : Array.Empty<string>())), string.Empty);
            }

            switch (schema.Format)
            {
                case "email":
                    yield return ("EmailAddress", string.Empty, string.Empty);
                    break;
                case "uri":
                    yield return ("Url", string.Empty, string.Empty);
                    break;
                case "byte":
                    yield return ("Base64String", string.Empty, string.Empty);
                    break;
            }
        }

        private static readonly string[] NullValue = { "null" };

        private static string Double(string value)
        {
            var number = Number(value);
            return number.Contains('.') || number.Contains('E') || number.Contains('e') ? number : number + ".0";
        }

        /// <summary>
        /// The attributes the resolver gives .NET's layer for a property, as expressions that construct them: the model's,
        /// each with Matchmark's own message, so a bad request reads the same whichever validator answers it (validation.md).
        /// A range is one attribute per bound, so each bound has its own message and a one-sided bound is checked too.
        /// </summary>
        private IEnumerable<string> ResolverAttributes(IOpenApiSchema schema, string type, bool nullable)
        {
            const string Ns = "global::System.ComponentModel.DataAnnotations.";
            string Message(string text) => $"ErrorMessage = {Naming.Literal(text)}";
            var array = (schema.Type ?? 0).HasFlag(JsonSchemaType.Array);
            if ((array ? schema.MinItems : schema.MinLength) is { } min)
            {
                yield return $"new {Ns}MinLengthAttribute({min.Invariant()}) {{ {Message(array ? $"Must have at least {min.Invariant()} items." : $"Must be at least {min.Invariant()} characters.")} }}";
            }

            if ((array ? schema.MaxItems : schema.MaxLength) is { } max)
            {
                yield return $"new {Ns}MaxLengthAttribute({max.Invariant()}) {{ {Message(array ? $"Must have at most {max.Invariant()} items." : $"Must be at most {max.Invariant()} characters.")} }}";
            }

            if (schema.Pattern is { } pattern)
            {
                yield return $"new {Ns}RegularExpressionAttribute({Naming.Literal(pattern)}) {{ {Message($"Must match {pattern}.")} }}";
            }

            if (schema.Minimum is { } minimum)
            {
                yield return $"new {Ns}RangeAttribute({Double(minimum)}, double.MaxValue) {{ {Message($"Must be at least {Number(minimum)}.")} }}";
            }

            if (schema.Maximum is { } maximum)
            {
                yield return $"new {Ns}RangeAttribute(double.MinValue, {Double(maximum)}) {{ {Message($"Must be at most {Number(maximum)}.")} }}";
            }

            if (schema.ExclusiveMinimum is { } above)
            {
                yield return $"new {Ns}RangeAttribute({Double(above)}, double.MaxValue) {{ MinimumIsExclusive = true, {Message($"Must be more than {Number(above)}.")} }}";
            }

            if (schema.ExclusiveMaximum is { } below)
            {
                yield return $"new {Ns}RangeAttribute(double.MinValue, {Double(below)}) {{ MaximumIsExclusive = true, {Message($"Must be less than {Number(below)}.")} }}";
            }

            foreach (var extra in AnnotationParts(schema, type, nullable, ValidationAttributes).Where(a => a.Name.StartsWith("global::", StringComparison.Ordinal)))
            {
                yield return $"new {extra.Name}Attribute({extra.Args})";
            }

            if (type != "string")
            {
                yield break;
            }

            if (Allowed(schema) is { Count: > 0 } texts)
            {
                var allowed = texts.Select(Naming.Literal).Concat(nullable ? NullValue : Array.Empty<string>());
                yield return $"new {Ns}AllowedValuesAttribute({string.Join(", ", allowed)}) {{ {Message("Must be one of " + string.Join(", ", texts) + ".")} }}";
            }

            switch (schema.Format)
            {
                case "email":
                    yield return $"new {Ns}EmailAddressAttribute {{ {Message("Must be an email address.")} }}";
                    break;
                case "uri":
                    yield return $"new {Ns}UrlAttribute {{ {Message("Must be an absolute URI.")} }}";
                    break;
                case "byte":
                    yield return $"new {Ns}Base64StringAttribute {{ {Message("Must be base64.")} }}";
                    break;
            }
        }

        /// <summary>The string values a property may take, from a string enum or a const; empty when it isn't so limited.</summary>
        private static List<string> Allowed(IOpenApiSchema schema)
            => schema.Enum is { Count: > 0 } values && values.All(v => v?.GetValueKind() == System.Text.Json.JsonValueKind.String)
                ? values.Select(v => v!.GetValue<string>()).ToList()
                : schema.Const is { } constant ? new List<string> { constant } : new List<string>();

        /// <summary>The keywords on a property that no attribute on .NET's layer checks (MM030): only the suite does.</summary>
        private IEnumerable<string> Unchecked(IOpenApiSchema schema)
        {
            if (!ValidationAttributes)
            {
                if (schema.MultipleOf is not null)
                {
                    yield return "multipleOf";
                }

                if (schema.UniqueItems == true)
                {
                    yield return "uniqueItems";
                }

                if (schema.MinProperties is not null || schema.MaxProperties is not null)
                {
                    yield return "minProperties or maxProperties";
                }
            }

            if (schema.Not is not null)
            {
                yield return "not";
            }

            if (schema.DependentRequired is { Count: > 0 })
            {
                yield return "dependentRequired";
            }
        }

        /// <summary>Each generated model property: its record, member, C# type, JSON name and attributes, for the resolver.</summary>
        private readonly List<(string Model, string Member, string Type, string Json, List<string> Attributes)> _modelProperties = new();

        /// <summary>
        /// The resolver for .NET's validation layer (validation.md, decision Q45), when the project references
        /// Microsoft.Extensions.Validation: every generated model by its properties' attributes, under the spec's names, and
        /// body parameters of those types, whose errors are keyed by the spec's JSON paths. Registered by Add{Api}Validation().
        /// </summary>
        /// <summary>Whether this emission wrote the resolver, so the server part registers it.</summary>
        private bool _resolverEmitted;

        private string? EmitValidationResolver(List<Operation> operations)
        {
            // Without the layer referenced, the server reports MM029 and keeps Matchmark's own validator alone.
            if (!NetValidation || _shared || _modelProperties.Count == 0)
            {
                return null;
            }

            const string V = "global::Microsoft.Extensions.Validation.";
            var w = new CodeWriter(_namespace);
            w.Line("#pragma warning disable ASP0029 // Microsoft.Extensions.Validation is experimental in .NET 10.");
            w.Usings("System", "System.Collections.Generic", "System.Linq", "System.Reflection", "System.Threading", "System.Threading.Tasks");
            w.Line("/// <summary>");
            w.Line($"/// Describes the {_apiName} models to .NET's validation layer, which Microsoft's validations generator can't see (they are");
            w.Line("/// generated too): each property's DataAnnotations, under the spec's JSON names. Body parameters of these types are");
            w.Line("/// validated, and their errors keyed by the spec's JSON paths (details.livesLeft), as the generated validator keys them.");
            w.Line("/// </summary>");
            w.Open($"public sealed class {_apiName}ValidationResolver : {V}IValidatableInfoResolver");
            w.Line("private static readonly Dictionary<Type, Model> Types = new()");
            w.Line("{");
            foreach (var model in _modelProperties.GroupBy(p => p.Model))
            {
                w.Line($"    [typeof({model.Key})] = new Model(typeof({model.Key}), new Member[]");
                w.Line("    {");
                foreach (var property in model)
                {
                    var attributes = property.Attributes;
                    w.Line($"        new Member(typeof({model.Key}), {Naming.Literal(property.Member)}, {Naming.Literal(property.Json)}{string.Concat(attributes.Select(a => ", " + a))}),");
                }

                w.Line("    }),");
            }

            w.Line("};");
            w.Line();

            // The generated endpoints' scalar parameters, by where they bind from and their name in the spec. A name two
            // operations constrain differently is left to the endpoint's own check.
            var scalars = operations.SelectMany(o => o.Parameters)
                .Where(p => p.Schema is not null && !p.Deep && !p.Enum && p.Attribute is "FromHeader" or "FromQuery" or "FromRoute")
                .Select(p => (Key: $"{p.Attribute.Substring(4).ToLowerInvariant()}:{p.Name}", Attributes: ResolverAttributes(p.Schema!, p.Type.TrimEnd('?'), nullable: true).ToList()))
                .Where(p => p.Attributes.Count > 0)
                .GroupBy(p => p.Key, StringComparer.Ordinal)
                .Where(g => g.Select(p => string.Join("|", p.Attributes)).Distinct().Count() == 1)
                .Select(g => g.First()).ToList();
            w.Line("private static readonly Dictionary<string, global::System.ComponentModel.DataAnnotations.ValidationAttribute[]> Scalars = new()");
            w.Line("{");
            foreach (var (key, attributes) in scalars)
            {
                w.Line($"    [{Naming.Literal(key)}] = new global::System.ComponentModel.DataAnnotations.ValidationAttribute[] {{ {string.Join(", ", attributes)} }},");
            }

            w.Line("};");
            w.Line();
            w.Open($"public bool TryGetValidatableTypeInfo(Type type, [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out {V}IValidatableInfo? validatableInfo)");
            w.Line("validatableInfo = Types.TryGetValue(type, out var info) ? info : null;");
            w.Line("return validatableInfo is not null;");
            w.Close();
            w.Line();
            w.Open($"public bool TryGetValidatableParameterInfo(ParameterInfo parameterInfo, [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out {V}IValidatableInfo? validatableInfo)");
            w.Line("var type = parameterInfo.ParameterType;");
            w.Line("if (Types.ContainsKey(Element(type) ?? type))");
            w.Line("{");
            w.Line("    validatableInfo = new Body(type, parameterInfo.Name ?? \"body\");");
            w.Line("    return true;");
            w.Line("}");
            w.Line();
            w.Line("// A path, query or header value, by its binding and its name in the spec, keyed as the spec names it.");
            w.Line("var from = parameterInfo.GetCustomAttributes(false).FirstOrDefault(a => a.GetType().Name is \"FromHeaderAttribute\" or \"FromQueryAttribute\" or \"FromRouteAttribute\");");
            w.Line("var name = from?.GetType().GetProperty(\"Name\")?.GetValue(from) as string ?? parameterInfo.Name;");
            w.Line("validatableInfo = from is not null && name is not null && Scalars.TryGetValue($\"{from.GetType().Name.Substring(4, from.GetType().Name.Length - 13).ToLowerInvariant()}:{name}\", out var attributes)");
            w.Line("    ? new Scalar(type, name, attributes) : null;");
            w.Line("return validatableInfo is not null;");
            w.Close();
            w.Line();
            w.Line("/// <summary>");
            w.Line("/// A model validated on .NET's layer outside ASP.NET Core (the generated client, question 3): its errors under the");
            w.Line("/// spec's JSON paths, $ for a rule broken by the whole value; empty when it's valid or isn't a generated model.");
            w.Line("/// </summary>");
            w.Open("public static async Task<Dictionary<string, string[]>> ValidateAsync(object value, CancellationToken cancellationToken = default)");
            w.Line("var errors = new Dictionary<string, string[]>();");
            w.Line("if (!Types.TryGetValue(value.GetType(), out var info))");
            w.Line("{");
            w.Line("    return errors;");
            w.Line("}");
            w.Line();
            w.Line($"var options = new {V}ValidationOptions();");
            w.Line($"options.Resolvers.Add(new {_apiName}ValidationResolver());");
            w.Line($"var context = new {V}ValidateContext {{ ValidationContext = new global::System.ComponentModel.DataAnnotations.ValidationContext(value), ValidationOptions = options }};");
            w.Line("await info.ValidateAsync(value, context, cancellationToken).ConfigureAwait(false);");
            w.Line("foreach (var error in context.ValidationErrors ?? new Dictionary<string, string[]>())");
            w.Line("{");
            w.Line("    errors[error.Key.Length == 0 ? \"$\" : JsonPath(value.GetType(), error.Key)] = error.Value;");
            w.Line("}");
            w.Line();
            w.Line("return errors;");
            w.Close();
            w.Line();
            w.Line("/// <summary>A C# error path (Details.LivesLeft, Tags[0]) as the spec's JSON path (details.livesLeft, tags[0]), from a root type.</summary>");
            w.Open("public static string JsonPath(Type root, string path)");
            w.Line("Type? type = root;");
            w.Line("var segments = new List<string>();");
            w.Line("foreach (var segment in path.Split('.'))");
            w.Line("{");
            w.Line("    var bracket = segment.IndexOf('[');");
            w.Line("    var name = bracket < 0 ? segment : segment.Substring(0, bracket);");
            w.Line("    if (name.Length == 0)");
            w.Line("    {");
            w.Line("        // An item of a list body: [0].");
            w.Line("        segments.Add(segment);");
            w.Line("        type = type is null ? null : Element(type);");
            w.Line("        continue;");
            w.Line("    }");
            w.Line();
            w.Line("    // A union's member is found on the type that has it (Details is a CatDetails or a DogDetails).");
            w.Line("    var declared = type is null ? null : Nullable.GetUnderlyingType(type) ?? type;");
            w.Line("    var member = declared is null ? null : Types.Where(t => declared.IsAssignableFrom(t.Key)).SelectMany(t => t.Value.Fields).FirstOrDefault(m => m.Clr == name);");
            w.Line("    segments.Add((member?.Json ?? name) + (bracket < 0 ? string.Empty : segment.Substring(bracket)));");
            w.Line("    type = member?.Type;");
            w.Line("    if (bracket >= 0 && type is not null)");
            w.Line("    {");
            w.Line("        type = Element(type);");
            w.Line("    }");
            w.Line("}");
            w.Line();
            w.Line("return string.Join(\".\", segments);");
            w.Close();
            w.Line();
            w.Line("/// <summary>A collection's item type, or null.</summary>");
            w.Line("private static Type? Element(Type type) => type == typeof(string) ? null : type.IsArray ? type.GetElementType()");
            w.Line("    : type.GetInterfaces().Concat(new[] { type }).FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))?.GetGenericArguments()[0];");
            w.Line();
            w.Line("#pragma warning disable CS9107 // the members are the base's too; they're kept to translate keys.");
            w.Line($"private sealed class Model(Type type, Member[] members) : {V}ValidatableTypeInfo(type, members)");
            w.Line("#pragma warning restore CS9107");
            w.Line("{");
            w.Line("    public IReadOnlyList<Member> Fields { get; } = members;");
            w.Line();
            w.Line("    protected override global::System.ComponentModel.DataAnnotations.ValidationAttribute[] GetValidationAttributes() => Array.Empty<global::System.ComponentModel.DataAnnotations.ValidationAttribute>();");
            w.Line("}");
            w.Line();
            w.Line("/// <summary>A property: found by its C# name, shown by its JSON name.</summary>");
            w.Line($"private sealed class Member(Type declaringType, string name, string json, params global::System.ComponentModel.DataAnnotations.ValidationAttribute[] attributes)");
            w.Line($"    : {V}ValidatablePropertyInfo(declaringType, declaringType.GetProperty(name)!.PropertyType, name, json)");
            w.Line("{");
            w.Line("    public string Clr { get; } = name;");
            w.Line();
            w.Line("    public string Json { get; } = json;");
            w.Line();
            w.Line("    public Type Type { get; } = declaringType.GetProperty(name)!.PropertyType;");
            w.Line();
            w.Line("    protected override global::System.ComponentModel.DataAnnotations.ValidationAttribute[] GetValidationAttributes() => attributes;");
            w.Line("}");
            w.Line();
            w.Line("/// <summary>A path, query or header value, under its name in the spec, with the spec's constraints.</summary>");
            w.Line($"private sealed class Scalar(Type type, string name, global::System.ComponentModel.DataAnnotations.ValidationAttribute[] attributes) : {V}ValidatableParameterInfo(type, name, name)");
            w.Line("{");
            w.Line("    protected override global::System.ComponentModel.DataAnnotations.ValidationAttribute[] GetValidationAttributes() => attributes;");
            w.Line("}");
            w.Line();
            w.Line("/// <summary>A body parameter: validated by .NET, then its errors keyed by the spec's JSON paths.</summary>");
            w.Line("#pragma warning disable CS9107 // the type is the base's too; it's kept to translate keys.");
            w.Line($"private sealed class Body(Type type, string name) : {V}ValidatableParameterInfo(type, name, name)");
            w.Line("#pragma warning restore CS9107");
            w.Line("{");
            w.Line("    private readonly Type _root = type;");
            w.Line();
            w.Line("    protected override global::System.ComponentModel.DataAnnotations.ValidationAttribute[] GetValidationAttributes() => Array.Empty<global::System.ComponentModel.DataAnnotations.ValidationAttribute>();");
            w.Line();
            w.Line($"    public override async Task ValidateAsync(object? value, {V}ValidateContext context, CancellationToken cancellationToken)");
            w.Line("    {");
            w.Line("        var errors = context.ValidationErrors;");
            w.Line("        context.ValidationErrors = new Dictionary<string, string[]>();");
            w.Line("        try");
            w.Line("        {");
            w.Line("            await base.ValidateAsync(value, context, cancellationToken).ConfigureAwait(false);");
            w.Line("        }");
            w.Line("        finally");
            w.Line("        {");
            w.Line("            foreach (var error in context.ValidationErrors ?? new Dictionary<string, string[]>())");
            w.Line("            {");
            w.Line("                // A rule broken by the whole body is keyed at it: $, as the generated validator keys it.");
            w.Line("                var key = error.Key.Length == 0 ? \"$\" : JsonPath(_root, error.Key);");
            w.Line("                errors ??= new Dictionary<string, string[]>();");
            w.Line("                errors[key] = errors.TryGetValue(key, out var had) ? had.Concat(error.Value).ToArray() : error.Value;");
            w.Line("            }");
            w.Line();
            w.Line("            context.ValidationErrors = errors;");
            w.Line("        }");
            w.Line("    }");
            w.Line("}");
            w.Close();
            w.Line();
            w.Line($"/// <summary>Registers .NET's validation layer with the {_apiName} resolver first (validation.md).</summary>");
            w.Open($"public static class {_apiName}ValidationExtensions");
            w.Line($"public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection Add{_apiName}Validation(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
            w.Line($"    => global::Microsoft.Extensions.DependencyInjection.ValidationServiceCollectionExtensions.AddValidation(services, options => options.Resolvers.Insert(0, new {_apiName}ValidationResolver()));");
            w.Close();
            _resolverEmitted = true;
            return w.ToString();
        }

    }
}
