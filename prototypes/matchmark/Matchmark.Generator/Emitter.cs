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
    /// <summary>
    /// Turns one OpenAPI document into C# source. Prototype scope: component schemas that are objects, string enums or
    /// discriminated oneOf unions; JSON bodies; path, query and header parameters.
    /// </summary>
    internal sealed partial class Emitter
    {
        /// <summary>Component schemas that map onto framework types instead of being generated.</summary>
        private static readonly Dictionary<string, string> WellKnownSchemas = new()
        {
            ["ProblemDetails"] = "global::Microsoft.AspNetCore.Mvc.ProblemDetails",
            ["HttpValidationProblemDetails"] = "global::Microsoft.AspNetCore.Http.HttpValidationProblemDetails",
        };

        private const string ValidationSchema = "HttpValidationProblemDetails";

        private const string FormMedia = "application/x-www-form-urlencoded";

        /// <summary>The framework types the schemas map onto: none when the contract owns its problem types.</summary>
        private Dictionary<string, string> Mapped => _ownProblems ? NoMapping : WellKnownSchemas;

        private static readonly Dictionary<string, string> NoMapping = new();

        /// <summary>
        /// MatchmarkProblems="Owned": the spec's problem schemas are generated like any other, so the contract needs no
        /// ASP.NET Core (a client-only package). The Server part writes ASP.NET Core's problem results, so it needs them.
        /// </summary>
        private readonly bool _ownProblems;

        private readonly OpenApiDocument _document;
        private readonly string _namespace;
        private readonly string _apiName;
        private readonly Location _location;

        /// <summary>Generated component schemas by name.</summary>
        private readonly Dictionary<string, IOpenApiSchema> _schemas = new(StringComparer.Ordinal);

        /// <summary>Component schemas that are a primitive or an array: a $ref to one is the type it stands for, not a class.</summary>
        private readonly Dictionary<string, IOpenApiSchema> _aliases = new(StringComparer.Ordinal);

        /// <summary>Inline objects (and allOfs) given generated types, by the schema instance, with the name chosen for each.</summary>
        private readonly Dictionary<IOpenApiSchema, string> _inline = new(Identity.Instance);

        private bool _collected;
        private readonly HashSet<IOpenApiSchema> _loose = new(Identity.Instance);

        /// <summary>Union member schema → (base type, discriminator property, discriminator value).</summary>
        private readonly Dictionary<string, (string Base, string Property, string Value)> _unionMembers = new(StringComparer.Ordinal);

        private readonly bool _describesItself;
        private readonly bool _hasFakes;

        /// <summary>Matchmark.Rules is referenced, so x-matchmark-rules and x-dependencies are enforced.</summary>
        private readonly bool _hasRules;

        /// <summary>The webhooks' API: its models are the main API's, so it doesn't write them again.</summary>
        private bool _shared;

        private bool _rulesReported;

        public Emitter(OpenApiDocument document, string ns, string apiName, Location location, bool describesItself = false, bool hasFakes = false,
            bool hasRules = false, bool ownProblems = false)
        {
            _ownProblems = ownProblems;
            _hasRules = hasRules;
            _describesItself = describesItself;
            _hasFakes = hasFakes;
            _document = document;
            _namespace = ns;
            _apiName = apiName;
            _location = location;
        }

        public List<Diagnostic> Diagnostics { get; } = new();

        private string ValidationClass => _apiName + "Validation";

        public Dictionary<string, string> Emit(bool contract = true, bool server = true, bool fakeServer = false, bool regression = false,
            IReadOnlyList<string>? workflows = null)
        {
            CollectSchemas();
            var operations = CollectOperations();
            var files = new Dictionary<string, string>();
            if (_ownProblems && (server || fakeServer))
            {
                Diagnostics.Add(Diagnostic.Create(Generator.Diagnostics.OwnedProblemsNeedNoServer, _location));
                server = fakeServer = false;
            }

            if (contract)
            {
                if (!_shared)
                {
                    files["Models"] = EmitModels();
                }

                files["Validation"] = EmitValidation(operations);
                if (EmitValidationResolver() is { } resolver)
                {
                    files["ValidationResolver"] = resolver;
                }
                files["Responses"] = EmitResponses(operations);
                files["Handlers"] = EmitHandlers(operations);
                files["Client"] = EmitClient(operations);
                if (EmitInvariants(operations) is { } invariants)
                {
                    files["Invariants"] = invariants;
                }
                if (_hasFakes)
                {
                    files["Fake"] = EmitFake(operations);
                }
            }

            if (server)
            {
                files["Endpoints"] = EmitEndpoints(operations);
                if (EmitInvariantsForEntityFramework() is { } entityFramework)
                {
                    files["Invariants.EntityFramework"] = entityFramework;
                }
                if (_describesItself)
                {
                    files["OpenApi"] = EmitOpenApi();
                }
            }

            if (fakeServer && _hasFakes)
            {
                files["FakeHosting"] = EmitFakeHosting(operations);
            }

            if (regression)
            {
                files["Regression"] = EmitRegression(workflows ?? Array.Empty<string>());
            }

            // Webhooks are operations the consumer implements: generated as an API of their own, {Api}Webhooks, from
            // the same components. Its handler interface and Map{Api}Webhooks are the typed receiver; its client sends
            // events (checking each against the spec); its fake records what a sender sent.
            if (_document.Webhooks is { Count: > 0 } webhooks && (contract || server))
            {
                var paths = new OpenApiPaths();
                foreach (var webhook in webhooks)
                {
                    paths["/" + webhook.Key] = webhook.Value;
                }

                var receiver = new Emitter(new OpenApiDocument { Info = _document.Info, Components = _document.Components, Paths = paths },
                    _namespace, _apiName + "Webhooks", _location, describesItself: false, _hasFakes, _hasRules, _ownProblems) { _shared = true };
                foreach (var file in receiver.Emit(contract, server))
                {
                    files["Webhooks." + file.Key] = file.Value;
                }

                Diagnostics.AddRange(receiver.Diagnostics.Where(d => !Diagnostics.Any(e => e.Id == d.Id && e.GetMessage(CultureInfo.InvariantCulture) == d.GetMessage(CultureInfo.InvariantCulture))));
            }

            // Callbacks: an operation's requests back to a URL its caller gave (subscribe, then receive). Generated like
            // webhooks, as {Api}Callbacks, each callback at /{name}: a typed receiver for the caller, a sender for the API.
            var callbacks = new OpenApiPaths();
            foreach (var item in _document.Paths?.Values ?? Enumerable.Empty<IOpenApiPathItem>())
            {
                foreach (var operation in item.Operations?.Values ?? Enumerable.Empty<OpenApiOperation>())
                {
                    foreach (var callback in operation.Callbacks ?? new Dictionary<string, IOpenApiCallback>())
                    {
                        if (callback.Value.PathItems?.Values.FirstOrDefault() is { } target && !callbacks.ContainsKey("/" + callback.Key))
                        {
                            callbacks["/" + callback.Key] = target;
                        }
                    }
                }
            }

            if (callbacks.Count > 0 && (contract || server))
            {
                var receiver = new Emitter(new OpenApiDocument { Info = _document.Info, Components = _document.Components, Paths = callbacks },
                    _namespace, _apiName + "Callbacks", _location, describesItself: false, _hasFakes, _hasRules, _ownProblems) { _shared = true };
                foreach (var file in receiver.Emit(contract, server))
                {
                    files["Callbacks." + file.Key] = file.Value;
                }

                Diagnostics.AddRange(receiver.Diagnostics.Where(d => !Diagnostics.Any(e => e.Id == d.Id && e.GetMessage(CultureInfo.InvariantCulture) == d.GetMessage(CultureInfo.InvariantCulture))));
            }

            return files;
        }

        // ---------------------------------------------------------------- the served OpenAPI document

        /// <summary>
        /// Configuration for ASP.NET Core's own OpenAPI document, so the server describes its schemas the way the spec
        /// does. It renames nothing the spec did not name and invents nothing: every rule below is driven by the spec's
        /// own unions and enums, and the round-trip test compares the result against the spec.
        /// </summary>
        private string EmitOpenApi(string? typesNamespace = null)
        {
            var w = new CodeWriter(_namespace);
            w.Usings("System.Collections.Generic", "System.Linq", "System.Threading.Tasks", "Microsoft.AspNetCore.OpenApi", "Microsoft.OpenApi");

            w.Open($"public static class {_apiName}OpenApi");

            w.Line("/// <summary>The spec's oneOf unions: base schema, discriminator property, and each member with its discriminator value.</summary>");
            w.Line("private static readonly (string Union, string Discriminator, (string Member, string Value)[] Members)[] Unions =");
            w.Line("[");
            foreach (var union in _unionMembers.GroupBy(m => m.Value.Base))
            {
                var members = string.Join(", ", union.Select(m => $"({Naming.Literal(Naming.Pascal(m.Key))}, {Naming.Literal(m.Value.Value)})"));
                w.Line($"    ({Naming.Literal(Naming.Pascal(union.Key))}, {Naming.Literal(union.First().Value.Property)}, [{members}]),");
            }

            w.Line("];");
            w.Line();
            var enums = _schemas.Where(s => IsEnum(s.Value)).Select(s => Naming.Literal(Naming.Pascal(s.Key)));
            w.Line($"private static readonly string[] StringEnums = [{string.Join(", ", enums)}];");
            w.Line();

            w.Line("/// <summary>");
            w.Line("/// Describes the generated types as the spec does: schemas named after their types, and oneOf unions, their members and");
            w.Line("/// string enums written in the spec's form rather than ASP.NET Core's.");
            w.Line("/// </summary>");
            w.Open($"public static OpenApiOptions Add{_apiName}Schemas(this OpenApiOptions options)");
            w.Line($"options.CreateSchemaReferenceId = type => type.Type.Namespace == {Naming.Literal(typesNamespace ?? _namespace)}");
            w.Line("    ? type.Type.Name");
            w.Line("    : OpenApiOptions.CreateDefaultSchemaReferenceId(type);");
            w.Line("options.AddDocumentTransformer((document, _, _) =>");
            w.Line("{");
            w.Line("    Describe(document);");
            w.Line("    return Task.CompletedTask;");
            w.Line("});");
            w.Line("return options;");
            w.Close();
            w.Line();

            w.Open("private static void Describe(OpenApiDocument document)");
            w.Line("var schemas = document.Components?.Schemas;");
            w.Open("if (schemas is null)");
            w.Line("return;");
            w.Close();
            w.Line();

            w.Open("foreach (var (union, discriminator, members) in Unions)");
            w.Open("if (!schemas.TryGetValue(union, out var found) || found is not OpenApiSchema schema || schema.Discriminator?.Mapping is not { } mapping)");
            w.Line("continue;");
            w.Close();
            w.Line();
            w.Line("// ASP.NET Core writes the union as anyOf over members it names '{Union}{Member}'; the spec writes oneOf over the members' own names.");
            w.Open("foreach (var (member, value) in members)");
            w.Open("if (mapping.TryGetValue(value, out var served) && served.Reference.Id is { } servedId && servedId != member && schemas.Remove(servedId, out var memberSchema))");
            w.Line("schemas[member] = memberSchema;");
            w.Close();
            w.Line();
            w.Line("mapping[value] = new OpenApiSchemaReference(member, document);");
            w.Line();
            w.Line("// The discriminator is an ordinary required string property of each member, not a single-value enum.");
            w.Open("if (schemas.TryGetValue(member, out var m) && m is OpenApiSchema memberObject)");
            w.Line("memberObject.Type ??= JsonSchemaType.Object;");
            w.Line("memberObject.Properties ??= new System.Collections.Generic.Dictionary<string, IOpenApiSchema>();");
            w.Line("memberObject.Properties[discriminator] = new OpenApiSchema { Type = JsonSchemaType.String };");
            w.Line("memberObject.Required ??= new System.Collections.Generic.HashSet<string>();");
            w.Line("memberObject.Required.Add(discriminator);");
            w.Close();
            w.Close();
            w.Line();
            w.Line("schema.OneOf = members.Select(m => (IOpenApiSchema)new OpenApiSchemaReference(m.Member, document)).ToList();");
            w.Line("schema.AnyOf = null;");
            w.Line("schema.Type = null;");
            w.Line("schema.Required = null;");
            w.Line("schema.Properties = null;");
            w.Close();
            w.Line();

            w.Open("foreach (var name in StringEnums)");
            w.Open("if (schemas.TryGetValue(name, out var e) && e is OpenApiSchema enumSchema)");
            w.Line("enumSchema.Type = JsonSchemaType.String;");
            w.Close();
            w.Close();
            w.Close();

            w.Close();
            return w.ToString();
        }

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
                    _modelProperties.Add((name, Member(name, schema, property.Key), $"{type}{(nullable ? "?" : string.Empty)}", property.Key, Annotations(property.Value, type, nullable, ValidationAttributes).ToList()));
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

            // A string enum the model keeps as text (an enum schema of its own is a C# enum, which needs none).
            if (schema.Enum is { Count: > 0 } values && values.All(v => v?.GetValueKind() == System.Text.Json.JsonValueKind.String))
            {
                // An optional or nullable value may be absent: AllowedValues refuses null unless it's listed.
                yield return ("AllowedValues", string.Join(", ", values.Select(v => Naming.Literal(v!.GetValue<string>())).Concat(nullable ? NullValue : Array.Empty<string>())), string.Empty);
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

        /// <summary>Each generated model property: its record, member, C# type, JSON name and attributes, for the resolver.</summary>
        private readonly List<(string Model, string Member, string Type, string Json, List<string> Attributes)> _modelProperties = new();

        /// <summary>
        /// The resolver for .NET's validation layer (validation.md, decision Q45), when the project references
        /// Microsoft.Extensions.Validation: every generated model by its properties' attributes, under the spec's names, and
        /// body parameters of those types, whose errors are keyed by the spec's JSON paths. Registered by Add{Api}Validation().
        /// </summary>
        private string? EmitValidationResolver()
        {
            // MM029 (NetValidationMissing) is reported once .NET's layer is the default; until then, Matchmark's own validator
            // is what every contract uses, and the resolver is an opt-in (Add{Api}Validation).
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
                    var attributes = property.Attributes.Select(a => Construct(a));
                    w.Line($"        new Member(typeof({model.Key}), {Naming.Literal(property.Member)}, {Naming.Literal(property.Json)}{string.Concat(attributes.Select(a => ", " + a))}),");
                }

                w.Line("    }),");
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
            w.Line("validatableInfo = Types.ContainsKey(Element(type) ?? type) ? new Body(type, parameterInfo.Name ?? \"body\") : null;");
            w.Line("return validatableInfo is not null;");
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
            w.Line("    var member = type is null ? null : Types.TryGetValue(Nullable.GetUnderlyingType(type) ?? type, out var info) ? info.Fields.FirstOrDefault(m => m.Clr == name) : null;");
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
            return w.ToString();
        }

        /// <summary>An attribute line, [global::…Range(1, 2, MinimumIsExclusive = true)], as an expression that constructs it.</summary>
        private static string Construct(string attribute)
        {
            var inner = attribute.Substring(1, attribute.Length - 2);
            var open = inner.IndexOf('(');
            var name = (open < 0 ? inner : inner.Substring(0, open)) + "Attribute";
            if (open < 0)
            {
                return $"new {name}()";
            }

            var args = inner.Substring(open + 1, inner.Length - open - 2);
            var named = System.Text.RegularExpressions.Regex.Match(args, @"(, )?(?<named>[A-Z]\w+ = .*)$");
            return named.Success
                ? $"new {name}({args.Substring(0, named.Index)}) {{ {named.Groups["named"].Value} }}"
                : $"new {name}({args})";
        }

        // ---------------------------------------------------------------- validation

        /// <summary>
        /// The translation of a binding failure (ASP.NET Core's key and message) into the spec's terms, shared by the
        /// generated endpoints and the scaffolded controllers. Found necessary by the regression suite generated from the spec.
        /// </summary>
        /// <summary>Forms (application/x-www-form-urlencoded), read into and written from the generated types, nested values in brackets.</summary>
        private const string FormMethods = @"/// <summary>
/// A form (application/x-www-form-urlencoded) as the type: nested values in brackets, as Stripe sends them (card[number],
/// expand[], items[0][price]); numbers and true or false read from their text. Throws JsonException, at the value's
/// path, for a form that doesn't fit the schema.
/// </summary>
public static T ReadForm<T>(IEnumerable<KeyValuePair<string, string>> fields)
{
    var root = new global::System.Text.Json.Nodes.JsonObject();
    foreach (var field in fields)
    {
        var keys = FormKeys(field.Key);
        var node = root;
        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i].Length == 0 ? node.Count.ToString(global::System.Globalization.CultureInfo.InvariantCulture) : keys[i];
            if (i == keys.Count - 1)
            {
                node[key] = field.Value;
            }
            else
            {
                if (node[key] is not global::System.Text.Json.Nodes.JsonObject next)
                {
                    next = new global::System.Text.Json.Nodes.JsonObject();
                    node[key] = next;
                }

                node = next;
            }
        }
    }

    return global::System.Text.Json.JsonSerializer.Deserialize<T>(FormArrays(root), FormRead)
        ?? throw new global::System.Text.Json.JsonException(""The form is empty."");
}

/// <summary>A deep-object query parameter's value (name[a][b]=v) as JSON, or the text of name=v; null when it isn't there.</summary>
public static global::System.Text.Json.Nodes.JsonNode? QueryNode(IEnumerable<KeyValuePair<string, string>> fields, string name)
{
    var own = fields.Where(f => f.Key == name || f.Key.StartsWith(name + ""["", System.StringComparison.Ordinal)).ToList();
    if (own.Count == 0)
    {
        return null;
    }

    if (own.All(f => f.Key == name))
    {
        return global::System.Text.Json.Nodes.JsonValue.Create(own[0].Value);
    }

    var root = new global::System.Text.Json.Nodes.JsonObject();
    foreach (var field in own.Where(f => f.Key != name))
    {
        var keys = FormKeys(""x"" + field.Key.Substring(name.Length));
        var node = root;
        for (var i = 1; i < keys.Count; i++)
        {
            var key = keys[i].Length == 0 ? node.Count.ToString(global::System.Globalization.CultureInfo.InvariantCulture) : keys[i];
            if (i == keys.Count - 1)
            {
                node[key] = field.Value;
            }
            else
            {
                if (node[key] is not global::System.Text.Json.Nodes.JsonObject next)
                {
                    next = new global::System.Text.Json.Nodes.JsonObject();
                    node[key] = next;
                }

                node = next;
            }
        }
    }

    return FormArrays(root);
}

/// <summary>The whole query as JSON (OpenAPI 3.2's querystring), nested values in brackets; null when it's empty.</summary>
public static global::System.Text.Json.Nodes.JsonNode? QueryAll(IEnumerable<KeyValuePair<string, string>> fields)
{
    var all = fields.ToList();
    if (all.Count == 0)
    {
        return null;
    }

    var root = new global::System.Text.Json.Nodes.JsonObject();
    foreach (var field in all)
    {
        var keys = FormKeys(field.Key);
        var node = root;
        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i].Length == 0 ? node.Count.ToString(global::System.Globalization.CultureInfo.InvariantCulture) : keys[i];
            if (i == keys.Count - 1)
            {
                node[key] = field.Value;
            }
            else
            {
                if (node[key] is not global::System.Text.Json.Nodes.JsonObject next)
                {
                    next = new global::System.Text.Json.Nodes.JsonObject();
                    node[key] = next;
                }

                node = next;
            }
        }
    }

    return FormArrays(root);
}

/// <summary>JSON as the type, numbers and true or false read from their text.</summary>
public static T FromNode<T>(global::System.Text.Json.Nodes.JsonNode node)
    => global::System.Text.Json.JsonSerializer.Deserialize<T>(node, FormRead) ?? throw new global::System.Text.Json.JsonException(""The value is null."");

/// <summary>A value as a deep-object query parameter's fields: name[a][b]=v, or name=v for a plain value.</summary>
public static IEnumerable<KeyValuePair<string, string>> QueryFields<T>(string name, T value)
    => FormFields(value).Select(f => new KeyValuePair<string, string>(
        f.Key.Length == 0 ? name : name + ""["" + (f.Key.IndexOf('[') is var open and >= 0 ? f.Key.Substring(0, open) + ""]"" + f.Key.Substring(open) : f.Key + ""]""), f.Value));

/// <summary>The type as form fields, nested values in brackets; absent values left out.</summary>
public static IEnumerable<KeyValuePair<string, string>> FormFields<T>(T value)
    => FormFlatten(global::System.Text.Json.JsonSerializer.SerializeToNode(value, FormWrite), null);

private static readonly global::System.Text.Json.JsonSerializerOptions FormRead = new(global::System.Text.Json.JsonSerializerDefaults.Web) { Converters = { new FormBoolean() } };

private static readonly global::System.Text.Json.JsonSerializerOptions FormWrite = new(global::System.Text.Json.JsonSerializerDefaults.Web)
{
    DefaultIgnoreCondition = global::System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
};

private static List<string> FormKeys(string key)
{
    var open = key.IndexOf('[');
    var keys = new List<string> { open < 0 ? key : key.Substring(0, open) };
    while (open >= 0 && key.IndexOf(']', open) is var close and > 0)
    {
        keys.Add(key.Substring(open + 1, close - open - 1));
        open = key.IndexOf('[', close);
    }

    return keys;
}

/// <summary>An object whose keys are 0, 1, 2... is an array.</summary>
private static global::System.Text.Json.Nodes.JsonNode? FormArrays(global::System.Text.Json.Nodes.JsonNode? node)
{
    if (node is not global::System.Text.Json.Nodes.JsonObject obj)
    {
        return node?.DeepClone();
    }

    var index = 0;
    if (obj.Count > 0 && obj.All(p => p.Key == (index++).ToString(global::System.Globalization.CultureInfo.InvariantCulture)))
    {
        return new global::System.Text.Json.Nodes.JsonArray(obj.Select(p => FormArrays(p.Value)).ToArray());
    }

    var copy = new global::System.Text.Json.Nodes.JsonObject();
    foreach (var pair in obj)
    {
        copy[pair.Key] = FormArrays(pair.Value);
    }

    return copy;
}

private static IEnumerable<KeyValuePair<string, string>> FormFlatten(global::System.Text.Json.Nodes.JsonNode? node, string? prefix)
{
    switch (node)
    {
        case global::System.Text.Json.Nodes.JsonObject obj:
            foreach (var pair in obj)
            {
                foreach (var field in FormFlatten(pair.Value, prefix is null ? pair.Key : prefix + ""["" + pair.Key + ""]""))
                {
                    yield return field;
                }
            }

            break;
        case global::System.Text.Json.Nodes.JsonArray array:
            for (var i = 0; i < array.Count; i++)
            {
                foreach (var field in FormFlatten(array[i], prefix + ""["" + i.ToString(global::System.Globalization.CultureInfo.InvariantCulture) + ""]""))
                {
                    yield return field;
                }
            }

            break;
        case global::System.Text.Json.Nodes.JsonValue value:
            yield return new KeyValuePair<string, string>(prefix ?? string.Empty, value.GetValueKind() switch
            {
                global::System.Text.Json.JsonValueKind.String => value.GetValue<string>(),
                global::System.Text.Json.JsonValueKind.True => ""true"",
                global::System.Text.Json.JsonValueKind.False => ""false"",
                _ => value.ToJsonString(),
            });
            break;
    }
}

/// <summary>true or false, from a form's text.</summary>
private sealed class FormBoolean : global::System.Text.Json.Serialization.JsonConverter<bool>
{
    public override bool Read(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Type typeToConvert, global::System.Text.Json.JsonSerializerOptions options)
        => reader.TokenType switch
        {
            global::System.Text.Json.JsonTokenType.True => true,
            global::System.Text.Json.JsonTokenType.False => false,
            global::System.Text.Json.JsonTokenType.String when reader.GetString() is ""true"" => true,
            global::System.Text.Json.JsonTokenType.String when reader.GetString() is ""false"" => false,
            _ => throw new global::System.Text.Json.JsonException(""Expected true or false.""),
        };

    public override void Write(global::System.Text.Json.Utf8JsonWriter writer, bool value, global::System.Text.Json.JsonSerializerOptions options)
        => writer.WriteBooleanValue(value);
}";

        private const string BindingErrorMethod = @"/// <summary>
/// A request that couldn't be bound, in the spec's terms: which value was wrong (<c>name</c>, <c>details.goodBoy</c>,
/// <c>petId</c>, or <c>body</c> for the whole body) and what was wrong with it. <paramref name=""key""/> is where ASP.NET Core
/// put the failure (a JSON path such as <c>$.name</c>, or a parameter name) and <paramref name=""message""/> its message.
/// The generated endpoints and the scaffolded controllers both answer through this, so they answer alike.
/// </summary>
public static (string Key, string Message) BindingError(string? key, string message)
{
    var at = key is null || key.Length == 0 || key == ""$"" ? string.Empty : key.StartsWith(""$."", System.StringComparison.Ordinal) ? key.Substring(2) : key.TrimStart('$');
    at = ParameterNames.TryGetValue(at, out var named) ? named : at;
    var missing = Regex.Match(message, ""missing required properties including: '([^']+)'"");
    if (missing.Success)
    {
        return (at.Length == 0 ? missing.Groups[1].Value : at + ""."" + missing.Groups[1].Value, $""'{missing.Groups[1].Value}' is required."");
    }

    var unmapped = Regex.Match(message, ""JSON property '([^']+)' could not be mapped"");
    if (unmapped.Success)
    {
        return (at.Length == 0 || at.EndsWith(unmapped.Groups[1].Value, System.StringComparison.Ordinal) ? (at.Length == 0 ? unmapped.Groups[1].Value : at) : at + ""."" + unmapped.Groups[1].Value,
            $""'{unmapped.Groups[1].Value}' isn't a property this object has."");
    }

    var body = at.Length == 0 || at == ""body"" ? ""body"" : at;
    var discriminator = Regex.Match(message, ""discriminator id '([^']*)'"");
    if (discriminator.Success)
    {
        return (body, $""'{discriminator.Groups[1].Value}' is not a known kind."");
    }

    if (message.Contains(""must specify a type discriminator""))
    {
        return (body, ""The type discriminator is missing."");
    }

    var value = Regex.Match(message, @""(?:value '([^']*)' is not valid|from \x22([^\x22]*)\x22)"");
    if (value.Success)
    {
        return (body, $""'{(value.Groups[1].Success ? value.Groups[1].Value : value.Groups[2].Value)}' is not a valid value."");
    }

    if (message.Contains(""request body is required"") || message.Contains(""was not provided from body""))
    {
        return (""body"", ""A request body is required."");
    }

    var type = Regex.Match(message, @""converted to ([\w.]+)\."");
    var expected = type.Groups[1].Value switch
    {
        ""System.String"" => ""a string"",
        ""System.Boolean"" => ""true or false"",
        ""System.Int16"" or ""System.Int32"" or ""System.Int64"" => ""an integer"",
        ""System.Single"" or ""System.Double"" or ""System.Decimal"" => ""a number"",
        ""System.DateTime"" or ""System.DateTimeOffset"" => ""a date-time"",
        ""System.Guid"" => ""a uuid"",
        _ => null,
    };
    return (body, expected is null ? ""The body is not valid JSON for this operation."" : $""Expected {expected}."");
}";

        private string EmitValidation(List<Operation> operations)
        {
            var w = new CodeWriter(_namespace);
            w.Usings("System.Collections.Generic", "System.Linq", "System.Text.RegularExpressions");

            w.Line("/// <summary>");
            w.Line("/// Validation generated from the schema constraints (required, minLength, maxLength, pattern, minimum, maximum).");
            w.Line("/// The endpoints run it on request bodies; the client runs it on response bodies.");
            w.Line("/// </summary>");
            w.Open($"public static class {ValidationClass}");
            w.Block(BindingErrorMethod);
            w.Line();

            foreach (var bodyType in _schemas.Where(s => !IsEnum(s.Value)).Select(s => Naming.Pascal(s.Key)))
            {
                w.Line($"/// <summary>Returns the errors keyed by JSON path, or <c>null</c> when <paramref name=\"value\"/> is valid.</summary>");
                w.Open($"public static Dictionary<string, string[]>? Validate({bodyType} value)");
                w.Line("var errors = new Errors();");
                w.Line("Check(value, string.Empty, errors);");
                w.Line("return errors.Result();");
                w.Close();
                w.Line();
            }

            var patterns = new List<string>();
            foreach (var pair in _schemas.Where(s => !IsEnum(s.Value)))
            {
                var name = Naming.Pascal(pair.Key);
                var schema = pair.Value;
                w.Open($"private static void Check({name} value, string path, Errors errors)");

                if (IsUnion(schema))
                {
                    w.Open("switch (value)");
                    foreach (var member in _unionMembers.Where(m => m.Value.Base == pair.Key))
                    {
                        w.Line($"case {Naming.Pascal(member.Key)} member: Check(member, path, errors); break;");
                    }

                    w.Close();
                    w.Close();
                    w.Line();
                    continue;
                }

                _unionMembers.TryGetValue(pair.Key, out var union);
                foreach (var property in schema.Properties!)
                {
                    if (union.Property == property.Key || TypeOf(property.Value, pair.Key) is not { } type)
                    {
                        continue;
                    }

                    var member = "value." + Member(name, schema, property.Key);
                    var jsonPath = $"path + {Naming.Literal(property.Key)}";
                    var required = schema.Required?.Contains(property.Key) == true;
                    var prop = property.Value;
                    var local = "v" + Member(name, schema, property.Key);

                    if (required && !Has(prop, JsonSchemaType.Null) && !IsValueType(type) && !IsEnumRef(prop))
                    {
                        w.Line($"if ({member} is null) errors.Add({jsonPath}, \"The field is required.\");");
                    }

                    var checks = new List<string>();
                    if (prop.MinLength is { } min)
                    {
                        checks.Add($"if ({local}.Length < {min.Invariant()}) errors.Add({jsonPath}, \"Must be at least {min.Invariant()} characters.\");");
                    }

                    if (prop.MaxLength is { } max)
                    {
                        checks.Add($"if ({local}.Length > {max.Invariant()}) errors.Add({jsonPath}, \"Must be at most {max.Invariant()} characters.\");");
                    }

                    if (prop.Pattern is { } pattern)
                    {
                        var index = patterns.Count;
                        patterns.Add(pattern);
                        checks.Add($"if (!Pattern{index.Invariant()}.IsMatch({local})) errors.Add({jsonPath}, {Naming.Literal("Must match " + pattern + ".")});");
                    }

                    if (type == "string" && FormatCheck(prop.Format, local) is { } format)
                    {
                        checks.Add($"if ({format.Condition}) errors.Add({jsonPath}, {Naming.Literal(format.Message)});");
                    }

                    // An enum written inline on a string property stays a string: its values are checked here.
                    if (type.TrimEnd('?') == "string" && !IsEnumRef(prop) && prop.Enum is { Count: > 0 } values
                        && values.Select(v => v is System.Text.Json.Nodes.JsonValue jv && jv.TryGetValue<string>(out var text) ? text : null).ToList() is var texts && texts.All(t => t is not null))
                    {
                        checks.Add($"if (!({local} is {string.Join(" or ", texts.Select(t => Naming.Literal(t!)))})) errors.Add({jsonPath}, {Naming.Literal("Must be one of " + string.Join(", ", texts) + ".")});");
                    }

                    if (prop.Minimum is { } minimum)
                    {
                        checks.Add($"if ({local} < {Number(minimum)}) errors.Add({jsonPath}, \"Must be at least {Number(minimum)}.\");");
                    }

                    if (prop.Maximum is { } maximum)
                    {
                        checks.Add($"if ({local} > {Number(maximum)}) errors.Add({jsonPath}, \"Must be at most {Number(maximum)}.\");");
                    }

                    // 3.1's numeric exclusive bounds (3.0's boolean form is read into these too).
                    if (prop.ExclusiveMinimum is { } above)
                    {
                        checks.Add($"if ({local} <= {Number(above)}) errors.Add({jsonPath}, \"Must be more than {Number(above)}.\");");
                    }

                    if (prop.ExclusiveMaximum is { } below)
                    {
                        checks.Add($"if ({local} >= {Number(below)}) errors.Add({jsonPath}, \"Must be less than {Number(below)}.\");");
                    }

                    if (RefId(prop) is { } refId && _schemas.ContainsKey(refId) && !IsEnumRef(prop))
                    {
                        checks.Add($"Check({local}, {jsonPath} + \".\", errors);");
                    }

                    if (Has(prop, JsonSchemaType.Array) && type.StartsWith("global::System.Collections.Generic.IReadOnlyList<", StringComparison.Ordinal))
                    {
                        // An array: its count, and each item as its own schema says.
                        if (prop.MinItems is { } minItems)
                        {
                            checks.Add($"if ({local}.Count < {minItems.Invariant()}) errors.Add({jsonPath}, \"Must have at least {minItems.Invariant()} items.\");");
                        }

                        if (prop.MaxItems is { } maxItems)
                        {
                            checks.Add($"if ({local}.Count > {maxItems.Invariant()}) errors.Add({jsonPath}, \"Must have at most {maxItems.Invariant()} items.\");");
                        }

                        if (prop.Items is { } items && ItemChecks(items, $"{jsonPath} + \"[\" + __i.ToString(global::System.Globalization.CultureInfo.InvariantCulture) + \"]\"", patterns) is { Count: > 0 } itemChecks)
                        {
                            checks.Add($"for (var __i = 0; __i < {local}.Count; __i++) {{ if ({local}[__i] is {{ }} __item) {{ {string.Join(" ", itemChecks)} }} }}");
                        }
                    }

                    if (checks.Count == 0)
                    {
                        continue;
                    }

                    // `is { } x` narrows both reference and nullable value types; a non-nullable value is always present.
                    var nullable = !required || Has(prop, JsonSchemaType.Null);
                    w.Open(IsValueType(type) && !nullable ? $"var {local} = {member};" : $"if ({member} is {{ }} {local})", braces: !(IsValueType(type) && !nullable));
                    foreach (var check in checks)
                    {
                        w.Line(check);
                    }

                    w.Close(braces: !(IsValueType(type) && !nullable));
                }

                foreach (var (rule, message) in Rules(schema))
                {
                    w.Line($"Rule(value, path, {Naming.Literal(rule)}, {Naming.Literal(message ?? $"Must satisfy {rule}.")}, errors);");
                }

                w.Close();
                w.Line();
            }

            if (operations.Any(o => o.Body is { Form: true } || o.Parameters.Any(p => p.Deep)))
            {
                w.Block(FormMethods);
                w.Line();
            }

            var renamed = operations.SelectMany(o => o.Parameters).Where(p => p.Identifier.TrimStart('@') != p.Name)
                .GroupBy(p => p.Identifier.TrimStart('@')).Select(g => g.First()).ToList();
            w.Line("/// <summary>The spec's name of each parameter whose C# name differs (xSignature is X-Signature), for binding errors.</summary>");
            w.Line($"private static readonly Dictionary<string, string> ParameterNames = new() {{ {string.Join(", ", renamed.Select(p => $"[{Naming.Literal(p.Identifier.TrimStart('@'))}] = {Naming.Literal(p.Name)}"))} }};");
            w.Line();
            w.Line("/// <summary>A text body against its schema (an empty one is missing when it's required): the errors, keyed body, or null.</summary>");
            w.Open("public static Dictionary<string, string[]>? CheckText(string text, int minLength, int? maxLength, string? pattern)");
            w.Line("var error = text.Length == 0 && minLength > 0 ? \"A request body is required.\"");
            w.Line("    : text.Length < minLength ? $\"Must be at least {minLength} characters.\"");
            w.Line("    : maxLength is { } max && text.Length > max ? $\"Must be at most {max} characters.\"");
            w.Line("    : pattern is not null && !Regex.IsMatch(text, pattern) ? $\"Must match {pattern}.\"");
            w.Line("    : null;");
            w.Line("return error is null ? null : new Dictionary<string, string[]> { [\"body\"] = new[] { error } };");
            w.Close();
            w.Line();
            w.Line("/// <summary>An enum from its text, by its name in the spec; false for anything else, a number included.</summary>");
            w.Open("public static bool TryEnum<T>(string text, out T value) where T : struct, global::System.Enum");
            w.Open("try");
            w.Line("value = global::System.Text.Json.JsonSerializer.Deserialize<T>(global::System.Text.Json.JsonSerializer.Serialize(text));");
            w.Line("return true;");
            w.Close();
            w.Open("catch (global::System.Text.Json.JsonException)");
            w.Line("value = default;");
            w.Line("return false;");
            w.Close();
            w.Close();
            w.Line();

            var refused = _schemas.Where(s => !IsEnum(s.Value) && s.Value.OneOf is not { Count: > 0 } && s.Value.Properties is { Count: > 0 })
                .Select(s => (Type: Naming.Pascal(s.Key), Names: s.Value.Properties!
                    .Where(p => s.Value.Required?.Contains(p.Key) != true && !Has(p.Value, JsonSchemaType.Null)
                                && !(_unionMembers.TryGetValue(s.Key, out var u) && u.Property == p.Key))
                    .Select(p => p.Key).ToList()))
                .Where(t => t.Names.Count > 0).ToList();
            w.Line("/// <summary>");
            w.Line("/// A JSON resolver modifier: refuses null, when read, for the optional properties the schema doesn't let be null, so that");
            w.Line("/// leaving one out and sending it as null aren't confused. The server's JSON options add it.");
            w.Line("/// </summary>");
            w.Open("public static void RefuseNulls(global::System.Text.Json.Serialization.Metadata.JsonTypeInfo typeInfo)");
            w.Open("if (!NotNull.TryGetValue(typeInfo.Type, out var names))");
            w.Line("return;");
            w.Close();
            w.Line();
            w.Open("foreach (var property in typeInfo.Properties)");
            w.Open("if (property.Set is { } set && global::System.Array.IndexOf(names, property.Name) >= 0)");
            w.Line("var name = property.Name;");
            w.Line("property.Set = (target, value) => set(target, value ?? throw new global::System.Text.Json.JsonException($\"'{name}' can't be null; leave it out instead.\"));");
            w.Close();
            w.Close();
            w.Close();
            w.Line();
            w.Line($"private static readonly Dictionary<global::System.Type, string[]> NotNull = new() {{ {string.Join(", ", refused.Select(t => $"[typeof({t.Type})] = new[] {{ {string.Join(", ", t.Names.Select(Naming.Literal))} }}"))} }};");
            w.Line();

            if (_hasRules)
            {
                // An absent optional value is absent to CEL's has(), not a null.
                w.Line("private static readonly global::System.Text.Json.JsonSerializerOptions RulesJson = new(global::System.Text.Json.JsonSerializerDefaults.Web) { DefaultIgnoreCondition = global::System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };");
                w.Line();
                w.Line("/// <summary>The rules a model breaks, as .NET's validation layer takes them (IValidatableObject): a result per broken rule.</summary>");
                w.Open("public static global::System.Collections.Generic.IEnumerable<global::System.ComponentModel.DataAnnotations.ValidationResult> Broken<T>(T value, params (string Rule, string Message)[] rules)");
                w.Line("var node = global::System.Text.Json.JsonSerializer.SerializeToNode(value, RulesJson);");
                w.Open("foreach (var rule in rules)");
                w.Open("if (global::Matchmark.Rules.Cel.Check(rule.Rule, node) is { Holds: false, Error: null })");
                w.Line("yield return new global::System.ComponentModel.DataAnnotations.ValidationResult(rule.Message);");
                w.Close();
                w.Close();
                w.Close();
                w.Line();
                w.Line("/// <summary>An x-matchmark-rules rule (CEL over the value as JSON); a value it can't be evaluated on is left to the handler.</summary>");
                w.Open("private static void Rule<T>(T value, string path, string rule, string message, Errors errors)");
                w.Open("if (global::Matchmark.Rules.Cel.Check(rule, global::System.Text.Json.JsonSerializer.SerializeToNode(value, RulesJson)) is { Holds: false, Error: null })");
                w.Line("errors.Add(path.Length == 0 ? \"$\" : path.TrimEnd('.'), message);");
                w.Close();
                w.Close();
                w.Line();
                foreach (var operation in operations.Where(o => o.Dependencies.Count > 0))
                {
                    var field = $"{operation.Name}Dependencies";
                    w.Line($"private static readonly global::Matchmark.Rules.Idl[] {field} = new[] {{ {string.Join(", ", operation.Dependencies.Select(d => $"global::Matchmark.Rules.Idl.Parse({Naming.Literal(d)})"))} }};");
                    w.Line();
                    w.Line($"/// <summary>The x-dependencies of {operation.Id} its parameters break, keyed by the first parameter each names; null when none.</summary>");
                    w.Open($"public static Dictionary<string, string[]>? Broken{operation.Name}Dependencies(Dictionary<string, string?> parameters)");
                    w.Line("var given = new Dictionary<string, string>();");
                    w.Open("foreach (var parameter in parameters)");
                    w.Line("if (parameter.Value is { } text) given[parameter.Key] = text;");
                    w.Close();
                    w.Line();
                    w.Line("var errors = new Errors();");
                    w.Open($"foreach (var dependency in {field})");
                    w.Open("if (!dependency.Holds(given))");
                    w.Line("errors.Add(dependency.Parameters.Count > 0 ? dependency.Parameters[0] : \"$\", $\"Must satisfy {dependency.Source}\");");
                    w.Close();
                    w.Close();
                    w.Line();
                    w.Line("return errors.Result();");
                    w.Close();
                    w.Line();
                }

                w.Line("/// <summary>A parameter as the text it travels as, for its dependencies.</summary>");
                w.Line("public static string? Text(object? value) => value switch { null => null, bool b => b ? \"true\" : \"false\", global::System.IFormattable f => f.ToString(null, global::System.Globalization.CultureInfo.InvariantCulture), _ => value.ToString() };");
                w.Line();
            }

            for (var i = 0; i < patterns.Count; i++)
            {
                w.Line($"private static readonly Regex Pattern{i.Invariant()} = new({Naming.Literal(patterns[i])}, RegexOptions.CultureInvariant);");
                w.Line();
            }

            w.Open("private sealed class Errors");
            w.Line("private Dictionary<string, List<string>>? _errors;");
            w.Line();
            w.Open("public void Add(string path, string message)");
            w.Line("_errors ??= new();");
            w.Line("if (!_errors.TryGetValue(path, out var list))");
            w.Line("{");
            w.Line("    _errors[path] = list = new();");
            w.Line("}");
            w.Line();
            w.Line("list.Add(message);");
            w.Close();
            w.Line();
            w.Line("public Dictionary<string, string[]>? Result()");
            w.Line("    => _errors is null ? null : System.Linq.Enumerable.ToDictionary(_errors, e => e.Key, e => e.Value.ToArray());");
            w.Close();

            w.Close();
            return w.ToString();
        }

        private static string Number(string value)
            => decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                ? d.ToString(CultureInfo.InvariantCulture)
                : value;

        // ---------------------------------------------------------------- operations

        private sealed class Body
        {
            public Body(string type, IOpenApiSchema schema)
            {
                Type = type;
                Schema = schema;
            }

            public string MediaType { get; set; } = "application/json";

            /// <summary>Sent as application/x-www-form-urlencoded, nested values in brackets (card[number]), as Stripe does.</summary>
            public bool Form => MediaType == FormMedia;

            /// <summary>Text (text/plain, text/markdown...): a string, read as it is.</summary>
            public bool Text => MediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase);

            /// <summary>A generated object type, so the validation class has a Validate for it (a list or a primitive body has none).</summary>
            public bool Validated => !Enum && !Type.Contains("global::", StringComparison.Ordinal) && Type is not ("string" or "int" or "long" or "float" or "double" or "decimal" or "bool");

            /// <summary>A body that's one of an enum's values.</summary>
            public bool Enum { get; set; }

            public string Type { get; }

            public IOpenApiSchema Schema { get; }
        }

        private sealed class Param
        {
            public Param(string name, string identifier, string type, string attribute, IOpenApiSchema? schema = null)
            {
                Schema = schema;
                Name = name;
                Identifier = identifier;
                Type = type;
                Attribute = attribute;
            }

            public string Name { get; }

            public string Identifier { get; }

            public string Type { get; }

            public string Attribute { get; }

            public IOpenApiSchema? Schema { get; }

            /// <summary>An enum: bound as text and read by its JSON name (Enum.TryParse would take a number, and not the spec's names).</summary>
            public bool Enum { get; set; }

            /// <summary>An object in the query, as a deep object (created[gte]=1&amp;created[lt]=9): read by the endpoint itself.</summary>
            public bool Deep { get; set; }

            /// <summary>OpenAPI 3.2's querystring: the whole query is this one object (limit=10&amp;text=tea).</summary>
            public bool Whole { get; set; }
        }

        private sealed class Response
        {
            public Response(int status, string? bodyType, string? mediaType = null, bool isValidation = false, string? schemaName = null)
            {
                Status = status;
                BodyType = bodyType;
                MediaType = mediaType;
                IsValidation = isValidation;
                SchemaName = schemaName;
            }

            public int Status { get; }

            public string? BodyType { get; }

            /// <summary>The documented content type of the body, which the client checks.</summary>
            public string? MediaType { get; }

            /// <summary>A 400 with HttpValidationProblemDetails on an operation with a body: written as ValidationProblem.</summary>
            public bool IsValidation { get; }

            /// <summary>The generated schema the body is, or holds a list of, for response validation; null for framework types.</summary>
            public string? SchemaName { get; set; }

            public bool IsList { get; set; }

            /// <summary>The body the fake answers with by default: the spec's example, or a valid sample built from the schema.</summary>
            public string? SampleJson { get; set; }

            /// <summary>A sequential body (OpenAPI 3.2's itemSchema, as JSON Lines): the body type is IAsyncEnumerable of this.</summary>
            public string? ItemType { get; set; }

            public bool IsStream => ItemType is not null;

            /// <summary>Server-sent events: each item an event, its data the item (as JSON, or the text of a string).</summary>
            public bool IsEventStream => IsStream && MediaType == "text/event-stream";

            /// <summary>A redirect (301, 302, 307, 308): its value is the Location, a string; it has no body.</summary>
            public bool IsRedirect { get; set; }

            /// <summary>The body's schema, for turning a sample into C#.</summary>
            public IOpenApiSchema? Schema { get; set; }

            public string CaseName => Status switch { 0 => "Default", -4 => "ClientError", -5 => "ServerError", _ => Naming.StatusNames[Status] };

            /// <summary>The spec's "default" response: any status it doesn't name. Its case carries the status.</summary>
            public bool IsDefault => Status == 0;

            /// <summary>A range (4XX, 5XX) or default: a case that carries the status it was answered with.</summary>
            public bool CarriesStatus => Status <= 0;

            /// <summary>Where it comes among the cases: a status first, then a range, then default.</summary>
            public int Order => Status > 0 ? 0 : Status < 0 ? 1 : 2;

            /// <summary>The status as a C# pattern: 404, >= 400 and &lt; 500, or _ (default).</summary>
            public string Pattern => Status switch { 0 => "_", -4 => ">= 400 and < 500", -5 => ">= 500 and < 600", _ => Status.Invariant() };

            /// <summary>
            /// A ProblemDetails body. TypedResults.NotFound(problem) and friends write it as application/json, so it
            /// goes out through TypedResults.Problem, which writes application/problem+json.
            /// </summary>
            public bool IsProblem => BodyType is { } type && WellKnownSchemas.ContainsValue(type);

            /// <summary>The <c>Microsoft.AspNetCore.Http.HttpResults</c> type this response is written as.</summary>
            public string ResultType => IsRedirect ? "RedirectHttpResult"
                : IsEventStream ? $"ServerSentEventsResult<{ItemType}>"
                : IsStream ? "PushStreamHttpResult"
                : Status == 401 && BodyType is null
                ? "UnauthorizedHttpResult"
                : IsValidation ? "ValidationProblem"
                : IsProblem ? "ProblemHttpResult"
                : !HasFactory ? BodyType is null ? "StatusCodeHttpResult" : $"JsonHttpResult<{BodyType}>"
                : BodyType is null ? CaseName : $"{CaseName}<{BodyType}>";

            /// <summary>A status with a TypedResults factory of its own (TypedResults.Conflict); the rest go out by number.</summary>
            public bool HasFactory => !IsStream && !CarriesStatus && !IsRedirect && (Status is 200 or 201 or 202 or 204 or 400 or 404 or 409 or 422 or 500 || Status == 401 && BodyType is null);

            /// <summary>
            /// Written by number (TypedResults.Json or StatusCode), so the result type carries no response metadata: the
            /// endpoint declares it with .Produces, as it does for problems.
            /// </summary>
            public bool NeedsProduces => !IsValidation && !IsProblem && !HasFactory && !CarriesStatus;

            /// <summary>The TypedResults call that produces it, given the body expression.</summary>
            /// <summary>The status a default case carries, from the expression for its body ("__case.Body" → "__case.Status").</summary>
            public static string StatusOf(string body) => (body.EndsWith(".Body", StringComparison.Ordinal) ? body.Substring(0, body.Length - 5) : body) + ".Status";

            public string Factory(string body) => (Status, BodyType is null) switch
            {
                _ when IsRedirect => $"TypedResults.Redirect({body}, permanent: {(Status is 301 or 308 ? "true" : "false")}, preserveMethod: {(Status is 307 or 308 ? "true" : "false")})",
                _ when CarriesStatus => BodyType is null ? $"TypedResults.StatusCode({StatusOf(body)})"
                    : IsProblem ? $"Problem({body}, {StatusOf(body)})"
                    : $"TypedResults.Json({body}, statusCode: {StatusOf(body)})",
                _ when IsEventStream => $"TypedResults.ServerSentEvents({body})",
                _ when IsStream => $"TypedResults.Stream(async __stream => {{ await foreach (var __item in {body}) {{ await global::System.Text.Json.JsonSerializer.SerializeAsync(__stream, __item, global::System.Text.Json.JsonSerializerOptions.Web); await __stream.WriteAsync(new byte[] {{ 10 }}); }} }}, {Naming.Literal(MediaType!)})",
                _ when IsValidation => $"TypedResults.ValidationProblem({body}.Errors, {body}.Detail, {body}.Instance, {body}.Title, {body}.Type)",
                _ when IsProblem => $"Problem({body}, {Status.Invariant()})",
                (_, true) when !HasFactory => $"TypedResults.StatusCode({Status.Invariant()})",
                (_, false) when !HasFactory => $"TypedResults.Json({body}, statusCode: {Status.Invariant()})",
                (201 or 202, false) => $"TypedResults.{CaseName}((string?)null, {body})",
                (201 or 202, true) => $"TypedResults.{CaseName}((string?)null)",
                (_, false) => $"TypedResults.{CaseName}({body})",
                _ => $"TypedResults.{CaseName}()",
            };
        }

        private sealed class Operation
        {
            public Operation(string id, string method, string path, string tag)
            {
                Id = id;
                Method = method;
                Path = path;
                Tag = tag;
            }

            public string Id { get; }

            public string Name => Naming.Pascal(Id);

            public string Method { get; }

            public string Path { get; }

            public string Tag { get; }

            public List<Param> Parameters { get; } = new();

            /// <summary>A parameter has constraints (bounds, lengths, a pattern) the endpoint checks.</summary>
            public bool ValidatesParameters => Parameters.Any(p => p.Deep || p.Enum || p.Schema is { } s && (s.Minimum is not null || s.Maximum is not null
                || s.MinLength is not null || s.MaxLength is not null || s.Pattern is not null));

            /// <summary>Its x-dependencies (IDL), enforced at the endpoint when Matchmark.Rules is referenced.</summary>
            public List<string> Dependencies { get; } = new();

            public Body? Body { get; set; }

            /// <summary>Its request body is required.</summary>
            public bool RequiredBody { get; set; }

            /// <summary>The response headers its responses document (ETag...), set by its handler through {Api}Headers.</summary>
            public SortedSet<string> ResponseHeaders { get; } = new(StringComparer.OrdinalIgnoreCase);

            public List<Response> Successes { get; } = new();

            public List<Response> Errors { get; } = new();

            /// <summary>
            /// A 400 whose body is HttpValidationProblemDetails. The generated boundary returns it when the body breaks the
            /// schema; it is also a case of the error union, so handlers can return domain validation failures the same way.
            /// </summary>
            public bool ValidationDocumented { get; set; }

            public bool SuccessIsUnion => Successes.Count > 1;

            public string SuccessType => SuccessIsUnion ? Name + "Success" : Successes[0].BodyType ?? "global::Resultful.Unit";

            public string ErrorType => Name + "Error";

            /// <summary>What the handler's Task completes with; null when it is a plain Task.</summary>
            public string? HandlerResultType => HandlerReturnType == "Task" ? null : HandlerReturnType.Substring(5, HandlerReturnType.Length - 6);

            public string HandlerReturnType => (Errors.Count, SuccessIsUnion || Successes[0].BodyType is not null) switch
            {
                (0, true) => $"Task<{SuccessType}>",
                (0, false) => "Task",
                (_, true) => $"Task<Result<{SuccessType}, {ErrorType}>>",
                (_, false) => $"Task<VoidResult<{ErrorType}>>",
            };

            public List<string> ResultTypes
            {
                get
                {
                    var types = Successes.Select(r => r.ResultType).ToList();
                    if (Body is not null || Dependencies.Count > 0 || ValidatesParameters)
                    {
                        types.Add("ValidationProblem");
                    }

                    types.AddRange(Errors.Select(r => r.ResultType));

                    // Every problem response is one ProblemHttpResult; its status comes from the body.
                    return types.Distinct().ToList();
                }
            }

            /// <summary>Results&lt;...&gt; takes at most six types; past that the endpoint returns IResult and declares every response with .Produces.</summary>
            public bool Untyped => ResultTypes.Count > 6;

            public string EndpointReturnType => Untyped ? "IResult" : ResultTypes.Count == 1 ? ResultTypes[0] : $"Results<{string.Join(", ", ResultTypes)}>";
        }

        private List<Operation> CollectOperations()
        {
            var operations = new List<Operation>();
            if (_document.Paths is null)
            {
                return operations;
            }

            foreach (var path in _document.Paths)
            {
                if (path.Value.Operations is null)
                {
                    continue;
                }

                foreach (var pair in path.Value.Operations)
                {
                    var method = pair.Key.Method.ToUpperInvariant();
                    var spec = pair.Value;
                    if (string.IsNullOrEmpty(spec.OperationId))
                    {
                        Diagnostics.Add(Diagnostic.Create(Generator.Diagnostics.MissingOperationId, _location, method, path.Key));
                        continue;
                    }

                    var tag = _shared ? _apiName : spec.Tags?.FirstOrDefault()?.Name ?? _apiName;
                    var operation = new Operation(spec.OperationId!, method, path.Key, tag);
                    if (TryBuild(operation, spec, path.Value.Parameters))
                    {
                        operations.Add(operation);
                    }
                }
            }

            return operations;
        }

        /// <summary>
        /// Reads a deep-object query parameter into its local: its bracketed keys as the type, null when absent (and,
        /// when it's required, the missing value is the problem). <paramref name="fail"/> throws or returns, given a
        /// JsonException placed at the parameter.
        /// </summary>
        private void EmitDeepRead(CodeWriter w, Param deep, string query, string cancellation, string fail)
        {
            var type = deep.Type.TrimEnd('?');
            var node = $"__node_{deep.Identifier.TrimStart('@')}";
            w.Line($"{deep.Type} {deep.Identifier};");
            w.Open("try");
            var fields = $"global::System.Linq.Enumerable.SelectMany({query}, q => global::System.Linq.Enumerable.Select(q.Value, v => new KeyValuePair<string, string>(q.Key, v ?? string.Empty)))";
            w.Line(deep.Whole
                ? $"var {node} = {ValidationClass}.QueryAll({fields});"
                : $"var {node} = {ValidationClass}.QueryNode({fields}, {Naming.Literal(deep.Name)});");
            if (!deep.Type.EndsWith("?", StringComparison.Ordinal))
            {
                w.Line($"if ({node} is null) throw new global::System.Text.Json.JsonException(\"The query is missing required properties including: '{deep.Name}'.\", \"$\", null, null);");
            }

            w.Line($"{deep.Identifier} = {node} is null ? default({deep.Type}) : {ValidationClass}.FromNode<{type}>({node});");
            w.Close();
            w.Open("catch (global::System.Text.Json.JsonException e)");
            w.Line(string.Format(CultureInfo.InvariantCulture, fail,
                $"new global::System.Text.Json.JsonException(e.Message, \"{(deep.Whole ? "$" : "$." + deep.Name)}\" + (e.Path ?? \"$\").Substring(1), null, null)"));
            w.Close();
            w.Line();
        }

        /// <summary>The check of a string's format the generated types don't already make (uri, email): its failing condition.</summary>
        private static (string Condition, string Message)? FormatCheck(string? format, string value) => format switch
        {
            "uri" => ($"!global::System.Uri.TryCreate({value}, global::System.UriKind.Absolute, out _)", "Must be an absolute URI."),
            "email" => ($"!global::System.Net.Mail.MailAddress.TryCreate({value}, out _)", "Must be an email address."),
            "byte" => ($"!global::System.Convert.TryFromBase64String({value}, new byte[{value}.Length], out _)", "Must be base64."),
            _ => null,
        };

        /// <summary>
        /// Reads each enum parameter from its text by its name in the spec (the enum's own converter), into its local;
        /// a value the spec doesn't name, or a required one missing, is the validation problem <paramref name="fail"/> gives.
        /// </summary>
        private void EmitEnumReads(CodeWriter w, Operation operation, string fail)
        {
            foreach (var parameter in operation.Parameters.Where(p => p.Enum))
            {
                var name = parameter.Identifier.TrimStart('@');
                var type = parameter.Type.TrimEnd('?');
                var problem = $"new Dictionary<string, string[]> {{ [{Naming.Literal(parameter.Name)}] = new[] {{ $\"'{{__text_{name}}}' is not one of the values the spec allows.\" }} }}";
                w.Line($"{parameter.Type} {parameter.Identifier} = default;");
                w.Open($"if (__text_{name} is not null)");
                w.Open($"if (!{ValidationClass}.TryEnum<{type}>(__text_{name}, out var __value_{name}))");
                w.Line(string.Format(CultureInfo.InvariantCulture, fail, problem));
                w.Close();
                w.Line();
                w.Line($"{parameter.Identifier} = __value_{name};");
                w.Close();
                if (!parameter.Type.EndsWith("?", StringComparison.Ordinal))
                {
                    w.Open("else");
                    w.Line(string.Format(CultureInfo.InvariantCulture, fail, $"new Dictionary<string, string[]> {{ [{Naming.Literal(parameter.Name)}] = new[] {{ \"The value is required.\" }} }}"));
                    w.Close();
                }

                w.Line();
            }
        }

        /// <summary>The checks of one array item (<c>__item</c>) at <paramref name="path"/>: its bounds, or its schema's Check.</summary>
        private List<string> ItemChecks(IOpenApiSchema items, string path, List<string> patterns)
        {
            var checks = new List<string>();
            if (RefId(items) is { } id && _schemas.ContainsKey(id) && !IsEnumRef(items))
            {
                checks.Add($"Check(__item, {path} + \".\", errors);");
                return checks;
            }

            if (Has(items, JsonSchemaType.String))
            {
                if (items.MinLength is { } min)
                {
                    checks.Add($"if (__item.Length < {min.Invariant()}) errors.Add({path}, \"Must be at least {min.Invariant()} characters.\");");
                }

                if (items.MaxLength is { } max)
                {
                    checks.Add($"if (__item.Length > {max.Invariant()}) errors.Add({path}, \"Must be at most {max.Invariant()} characters.\");");
                }

                if (items.Pattern is { } pattern)
                {
                    var index = patterns.Count;
                    patterns.Add(pattern);
                    checks.Add($"if (!Pattern{index.Invariant()}.IsMatch(__item)) errors.Add({path}, {Naming.Literal("Must match " + pattern + ".")});");
                }

                if (FormatCheck(items.Format, "__item") is { } format)
                {
                    checks.Add($"if ({format.Condition}) errors.Add({path}, {Naming.Literal(format.Message)});");
                }
            }
            else if (Has(items, JsonSchemaType.Integer) || Has(items, JsonSchemaType.Number))
            {
                if (items.Minimum is { } minimum)
                {
                    checks.Add($"if (__item < {Number(minimum)}) errors.Add({path}, \"Must be at least {Number(minimum)}.\");");
                }

                if (items.Maximum is { } maximum)
                {
                    checks.Add($"if (__item > {Number(maximum)}) errors.Add({path}, \"Must be at most {Number(maximum)}.\");");
                }
            }

            return checks;
        }

        /// <summary>The checks of an endpoint's bound parameters, each adding to <c>__invalid</c>, by the spec's constraints.</summary>
        private IEnumerable<string> ParameterChecks(Operation operation)
        {
            foreach (var (parameter, index) in operation.Parameters.Select((p, i) => (p, i)))
            {
                if (parameter.Schema is not { } schema)
                {
                    continue;
                }

                // A deep object is one of the generated types: its own Validate, each error under the parameter's name.
                if (parameter.Deep && _schemas.Keys.Select(Naming.Pascal).Contains(parameter.Type.TrimEnd('?')))
                {
                    var prefix = parameter.Whole ? string.Empty : parameter.Name + ".";
                    yield return $"if ({parameter.Identifier} is {{ }} __deep{index.Invariant()} && {ValidationClass}.Validate(__deep{index.Invariant()}) is {{ }} __errors{index.Invariant()}) foreach (var __e in __errors{index.Invariant()}) __invalid[{Naming.Literal(prefix)} + __e.Key] = __e.Value;";
                    continue;
                }

                var type = parameter.Type.TrimEnd('?');
                var key = Naming.Literal(parameter.Name);

                // A pattern variable in an if's condition is in scope for the whole block: one name per check.
                var checks = 0;
                string local = string.Empty;
                string When() => $"{parameter.Identifier} is {{ }} {local = $"__p{index.Invariant()}_{(checks++).Invariant()}"}";
                if (type is "int" or "long" or "float" or "double" or "decimal" or "short")
                {
                    if (schema.Minimum is { } minimum)
                    {
                        yield return $"if ({When()} && {local} < {Number(minimum)}) __invalid[{key}] = new[] {{ \"Must be at least {Number(minimum)}.\" }};";
                    }

                    if (schema.Maximum is { } maximum)
                    {
                        yield return $"if ({When()} && {local} > {Number(maximum)}) __invalid[{key}] = new[] {{ \"Must be at most {Number(maximum)}.\" }};";
                    }
                }
                else if (type == "string")
                {
                    if (schema.MinLength is { } min)
                    {
                        yield return $"if ({When()} && {local}.Length < {min.Invariant()}) __invalid[{key}] = new[] {{ \"Must be at least {min.Invariant()} characters.\" }};";
                    }

                    if (schema.MaxLength is { } max)
                    {
                        yield return $"if ({When()} && {local}.Length > {max.Invariant()}) __invalid[{key}] = new[] {{ \"Must be at most {max.Invariant()} characters.\" }};";
                    }

                    if (schema.Pattern is { } pattern)
                    {
                        yield return $"if ({When()} && !global::System.Text.RegularExpressions.Regex.IsMatch({local}, {Naming.Literal(pattern)})) __invalid[{key}] = new[] {{ {Naming.Literal("Must match " + pattern + ".")} }};";
                    }

                    if (FormatCheck(schema.Format, "__fmt") is { } format)
                    {
                        var when = When();
                        yield return $"if ({when} && {format.Condition.Replace("__fmt", local)}) __invalid[{key}] = new[] {{ {Naming.Literal(format.Message)} }};";
                    }
                }
            }
        }

        /// <summary>A schema's x-matchmark-rules (rule, message), when Matchmark.Rules is referenced to enforce them.</summary>
        private IEnumerable<(string Rule, string? Message)> Rules(IOpenApiSchema schema)
        {
            if (Extension(schema.Extensions, "x-matchmark-rules") is not System.Text.Json.Nodes.JsonArray rules || !Enforced("x-matchmark-rules"))
            {
                yield break;
            }

            foreach (var rule in rules.OfType<System.Text.Json.Nodes.JsonObject>())
            {
                if (rule["rule"] is System.Text.Json.Nodes.JsonValue text && text.GetValueKind() == System.Text.Json.JsonValueKind.String)
                {
                    yield return (text.GetValue<string>(), rule["message"] is System.Text.Json.Nodes.JsonValue m && m.GetValueKind() == System.Text.Json.JsonValueKind.String ? m.GetValue<string>() : null);
                }
            }
        }

        /// <summary>Whether invariants are enforced; reports once (MM025) when the spec declares some and nothing can enforce them.</summary>
        private bool Enforced(string what)
        {
            if (!_hasRules && !_rulesReported)
            {
                _rulesReported = true;
                Diagnostics.Add(Diagnostic.Create(Generator.Diagnostics.RulesNotEnforced, _location, what));
            }

            return _hasRules;
        }

        private static System.Text.Json.Nodes.JsonNode? Extension(IDictionary<string, IOpenApiExtension>? extensions, string name)
            => extensions is not null && extensions.TryGetValue(name, out var extension) && extension is JsonNodeExtension node ? node.Node : null;

        private bool TryBuild(Operation operation, OpenApiOperation spec, IList<IOpenApiParameter>? pathParameters)
        {
            if (Extension(spec.Extensions, "x-dependencies") is System.Text.Json.Nodes.JsonArray dependencies && dependencies.Count > 0 && Enforced("x-dependencies"))
            {
                operation.Dependencies.AddRange(dependencies.OfType<System.Text.Json.Nodes.JsonValue>()
                    .Where(d => d.GetValueKind() == System.Text.Json.JsonValueKind.String).Select(d => d.GetValue<string>()));
            }

            var context = $"Operation '{operation.Id}'";
            var parameters = (pathParameters ?? Array.Empty<IOpenApiParameter>()).Concat(spec.Parameters ?? Array.Empty<IOpenApiParameter>());
            foreach (var parameter in parameters)
            {
                // OpenAPI 3.2's querystring: one parameter that is the whole query, described by its content.
                if (parameter.In == ParameterLocation.QueryString)
                {
                    var whole = parameter.Schema ?? parameter.Content?.Values.FirstOrDefault()?.Schema;
                    if (whole is null || TypeOf(whole, $"{context} parameter '{parameter.Name}'", operation.Name + Naming.Pascal(parameter.Name!)) is not { } wholeType)
                    {
                        Unsupported($"{context} parameter '{parameter.Name}'", "a querystring that isn't an object the generator can type");
                        return false;
                    }

                    operation.Parameters.Add(new Param(parameter.Name!, Naming.Camel(parameter.Name!), wholeType + (parameter.Required ? string.Empty : "?"), "FromQuery", whole)
                    {
                        Deep = true,
                        Whole = true,
                    });
                    continue;
                }

                var attribute = parameter.In switch
                {
                    ParameterLocation.Path => "FromRoute",
                    ParameterLocation.Query => "FromQuery",
                    ParameterLocation.Header => "FromHeader",
                    _ => null,
                };

                // An object in the query (style: deepObject) gets a type of its own, named for the operation and parameter.
                var hint = parameter.In == ParameterLocation.Query ? operation.Name + Naming.Pascal(parameter.Name!) : null;

                // A oneOf or anyOf of scalars (GitHub's workflow_id: an integer or a file name) travels as text: a string.
                var scalars = parameter.Schema is { } union && (union.OneOf ?? union.AnyOf) is { Count: > 0 } members
                              && members.All(m => ((m.Type ?? 0) & ~JsonSchemaType.Null) is JsonSchemaType.String or JsonSchemaType.Integer or JsonSchemaType.Number or JsonSchemaType.Boolean);
                if (attribute is null || parameter.Schema is null || (scalars ? "string" : TypeOf(parameter.Schema, $"{context} parameter '{parameter.Name}'", hint)) is not { } type)
                {
                    Unsupported($"{context} parameter '{parameter.Name}'", $"{parameter.In} parameters");
                    return false;
                }

                var nullable = !parameter.Required ? "?" : string.Empty;
                operation.Parameters.Add(new Param(parameter.Name!, Naming.Camel(parameter.Name!), type + nullable, attribute, parameter.Schema)
                {
                    Deep = parameter.In == ParameterLocation.Query && (parameter.Style == ParameterStyle.DeepObject || IsObject(parameter.Schema)),
                    Enum = !scalars && IsEnumRef(parameter.Schema),
                });
            }

            if (spec.RequestBody is { } requestBody)
            {
                if (JsonSchema(requestBody.Content) is { } bodySchema)
                {
                    if (TypeOf(bodySchema, context + " request body", operation.Name + "Request") is not { } bodyType)
                    {
                        Unsupported(context + " request body", "request bodies must be JSON the generator can type");
                        return false;
                    }

                    // An enum body (tictactoe's mark) is checked by its converter as it's read: no Validate for it.
                    operation.Body = new Body(bodyType, bodySchema) { Enum = IsEnumRef(bodySchema) || IsEnum(bodySchema) };
                }
                else if (requestBody.Content?.TryGetValue(FormMedia, out var form) == true && form.Schema is { } formSchema)
                {
                    // A form with no fields (Stripe declares one on its GETs) is no body at all.
                    if (formSchema.Properties is { Count: > 0 } || formSchema.AdditionalProperties is not null)
                    {
                        if (TypeOf(formSchema, context + " request body", operation.Name + "Request") is not { } formType)
                        {
                            Unsupported(context + " request body", "form bodies must be objects the generator can type");
                            return false;
                        }

                        operation.Body = new Body(formType, formSchema) { MediaType = FormMedia };
                    }
                }
                else if (requestBody.Content?.FirstOrDefault(c => c.Key.StartsWith("text/", StringComparison.OrdinalIgnoreCase)) is { Key: not null } text
                         && (text.Value.Schema is null || Has(text.Value.Schema, JsonSchemaType.String)))
                {
                    // Text (GitHub's markdown to render): the body is a string, read as it is.
                    operation.Body = new Body("string", text.Value.Schema ?? new OpenApiSchema { Type = JsonSchemaType.String }) { MediaType = text.Key };
                    operation.RequiredBody = requestBody.Required;
                }
                else
                {
                    Unsupported(context + " request body", "request bodies must be JSON, a form or text the generator can type");
                    return false;
                }
            }

            foreach (var pair in spec.Responses ?? new OpenApiResponses())
            {
                foreach (var header in pair.Value.Headers?.Keys ?? Enumerable.Empty<string>())
                {
                    if (!string.Equals(header, "Content-Type", StringComparison.OrdinalIgnoreCase))
                    {
                        operation.ResponseHeaders.Add(header);
                    }
                }

                // "default": every status the spec doesn't name, an error case that carries its status.
                // 4XX and 5XX: any status in the range, like default. (A range of successes or redirects isn't generated.)
                var status = pair.Key switch { "default" => 0, "4XX" or "4xx" => -4, "5XX" or "5xx" => -5, _ => 1 };
                if (status > 0 && (!int.TryParse(pair.Key, NumberStyles.None, CultureInfo.InvariantCulture, out status) || !Naming.StatusNames.ContainsKey(status)))
                {
                    Unsupported($"{context} response '{pair.Key}'", "status codes outside " + string.Join(", ", Naming.StatusNames.Keys));
                    return false;
                }

                string? bodyType = null, mediaType = null, schemaName = null;
                var isValidation = false;
                var isList = false;
                if (JsonMedia(pair.Value.Content) is { } media)
                {
                    mediaType = media.Key;
                    var responseSchema = media.Value.Schema!;
                    isValidation = operation.Body is not null && status == 400 && RefId(responseSchema) == ValidationSchema;
                    operation.ValidationDocumented |= isValidation;

                    bodyType = TypeOf(responseSchema, $"{context} response '{pair.Key}'", $"{operation.Name}{(status <= 0 ? new Response(status, null).CaseName : Naming.StatusNames[status])}Body");
                    if (bodyType is null)
                    {
                        return false;
                    }

                    var target = responseSchema.Items is { } items && Has(responseSchema, JsonSchemaType.Array) ? items : responseSchema;
                    isList = !ReferenceEquals(target, responseSchema);
                    schemaName = RefId(target) is { } id && _schemas.TryGetValue(id, out var s) && !IsEnum(s) ? Naming.Pascal(id) : null;
                }
                else if (pair.Value.Content?.FirstOrDefault(c => c.Value.ItemSchema is not null) is { Key: not null } sequential)
                {
                    // OpenAPI 3.2's sequential media types, as IAsyncEnumerable<T>: JSON Lines, each line a T; server-sent
                    // events, each event's data a T. A server-sent event's itemSchema describes the event (data, event,
                    // id, retry): T is its data's contentSchema, or a string; an itemSchema without data is T itself.
                    var payload = sequential.Value.ItemSchema!;
                    string? itemType;
                    if (sequential.Key == "text/event-stream" && payload.Properties?.TryGetValue("data", out var data) == true)
                    {
                        var content = (data as IOpenApiSchemaMissingProperties)?.ContentSchema is { } contentSchema ? RefId(contentSchema) : null;
                        itemType = content is not null && _schemas.ContainsKey(content) ? Naming.Pascal(content) : "string";
                        payload = content is not null && _document.Components?.Schemas?.TryGetValue(content, out var contentTarget) == true ? new OpenApiSchemaReference(content, _document) : data;
                    }
                    else if (sequential.Key is "application/jsonl" or "application/x-ndjson" or "text/event-stream")
                    {
                        itemType = TypeOf(payload, $"{context} response '{pair.Key}' item", $"{operation.Name}{(status <= 0 ? new Response(status, null).CaseName : Naming.StatusNames[status])}Item");
                    }
                    else
                    {
                        itemType = null;
                    }

                    if (itemType is null)
                    {
                        Unsupported($"{context} response '{pair.Key}' ({sequential.Key})", "sequential media types other than JSON Lines and server-sent events");
                        return false;
                    }

                    var stream = new Response(status, $"global::System.Collections.Generic.IAsyncEnumerable<{itemType}>", sequential.Key, false,
                        RefId(payload) is { } itemId && _schemas.TryGetValue(itemId, out var itemSchema) && !IsEnum(itemSchema) ? Naming.Pascal(itemId) : null)
                    {
                        ItemType = itemType,
                        SampleJson = SampleOf(payload, context, 0) is { } item ? new System.Text.Json.Nodes.JsonArray(item.DeepClone()).ToJsonString() : null,
                    };
                    (status is >= 200 and < 300 ? operation.Successes : operation.Errors).Add(stream);
                    continue;
                }

                var sample = JsonMedia(pair.Value.Content) is { } m ? Sample(m.Value, $"{context} response '{pair.Key}'") : null;
                (status is >= 200 and < 300 ? operation.Successes : operation.Errors)
                    .Add(new Response(status, bodyType, mediaType, isValidation, schemaName)
                    {
                        IsList = isList,
                        SampleJson = sample,
                        Schema = JsonSchema(pair.Value.Content),
                    });
            }

            // An operation whose answer is a redirect (GitHub's downloads: a 302 to the file): the redirect is its success,
            // and its value the Location.
            if (operation.Successes.Count == 0 && operation.Errors.Where(e => e.Status is 301 or 302 or 307 or 308).ToList() is { Count: > 0 } redirects)
            {
                foreach (var redirect in redirects)
                {
                    operation.Errors.Remove(redirect);
                    operation.Successes.Add(new Response(redirect.Status, "string") { IsRedirect = true, SampleJson = "\"https://example.com/\"" });
                }
            }

            if (operation.Successes.Count == 0)
            {
                Unsupported(context, "operations without a 2xx response");
                return false;
            }

            if (operation.Body is not null && !operation.ValidationDocumented)
            {
                Diagnostics.Add(Diagnostic.Create(Generator.Diagnostics.UndocumentedValidation, _location, operation.Id));
            }



            return true;
        }

        private static IOpenApiSchema? JsonSchema(IDictionary<string, IOpenApiMediaType>? content)
            => JsonMedia(content)?.Value.Schema;

        private static KeyValuePair<string, IOpenApiMediaType>? JsonMedia(IDictionary<string, IOpenApiMediaType>? content)
        {
            if (content is null)
            {
                return null;
            }

            foreach (var pair in content)
            {
                if (pair.Key.StartsWith("application/", StringComparison.OrdinalIgnoreCase)
                    && pair.Key.EndsWith("json", StringComparison.OrdinalIgnoreCase)
                    && pair.Value.Schema is not null)
                {
                    return pair;
                }
            }

            return null;
        }

        // ---------------------------------------------------------------- responses (OneOf unions)

        private string EmitResponses(List<Operation> operations)
        {
            var w = new CodeWriter(_namespace);
            w.Usings("OneOf");

            foreach (var operation in operations)
            {
                if (operation.SuccessIsUnion)
                {
                    EmitUnion(w, operation, operation.SuccessType, operation.Successes, "The success responses");
                }

                if (operation.Errors.Count > 0)
                {
                    EmitUnion(w, operation, operation.ErrorType, operation.Errors, "The error responses");
                }
            }

            return w.ToString();
        }

        private static void EmitUnion(CodeWriter w, Operation operation, string unionName, List<Response> cases, string summary)
        {
            foreach (var response in cases)
            {
                var caseName = operation.Name + response.CaseName;
                if (response.CarriesStatus)
                {
                    w.Line($"/// <summary>Any status the spec doesn't name for <c>{operation.Id}</c> (its default response), with the status.</summary>");
                    w.Line(response.BodyType is null
                        ? $"public sealed record {caseName}(int Status);"
                        : $"public sealed record {caseName}(int Status, {response.BodyType} Body);");
                    w.Line();
                    continue;
                }

                w.Line($"/// <summary>{response.Status.Invariant()} {response.CaseName} from <c>{operation.Id}</c>.</summary>");
                w.Line(response.BodyType is null
                    ? $"public sealed record {caseName};"
                    : $"public sealed record {caseName}({response.BodyType} Body);");
                w.Line();
            }

            var caseTypes = string.Join(", ", cases.Select(c => operation.Name + c.CaseName));
            w.Line($"/// <summary>{summary} of <c>{operation.Id}</c>, from the spec.</summary>");
            w.Open($"public sealed class {unionName} : OneOfBase<{caseTypes}>");
            w.Line($"private {unionName}(OneOf<{caseTypes}> value) : base(value) {{ }}");
            foreach (var response in cases)
            {
                var caseName = operation.Name + response.CaseName;
                w.Line();
                w.Line($"public static implicit operator {unionName}({caseName} value) => new(value);");
                w.Line();
                w.Line((response.CarriesStatus, response.BodyType is null) switch
                {
                    (true, true) => $"public static {unionName} {response.CaseName}(int status) => new {caseName}(status);",
                    (true, false) => $"public static {unionName} {response.CaseName}(int status, {response.BodyType} body) => new {caseName}(status, body);",
                    (false, true) => $"public static {unionName} {response.CaseName}() => new {caseName}();",
                    _ => $"public static {unionName} {response.CaseName}({response.BodyType} body) => new {caseName}(body);",
                });
            }

            w.Close();
            w.Line();
        }

        // ---------------------------------------------------------------- handlers

        private string EmitHandlers(List<Operation> operations)
        {
            var w = new CodeWriter(_namespace);
            w.Usings("System.Threading", "System.Threading.Tasks", "Resultful");

            foreach (var group in operations.GroupBy(o => o.Tag))
            {
                w.Line($"/// <summary>Implement this for the <c>{group.Key}</c> operations. Return values carry the spec's responses; HTTP is handled by the generated endpoints.</summary>");
                w.Open($"public interface {HandlerInterface(group.Key)}");
                foreach (var operation in group)
                {
                    var args = operation.Parameters.Select(p => $"{p.Type} {p.Identifier}").ToList();
                    if (operation.Body is not null)
                    {
                        args.Add($"{operation.Body.Type} body");
                    }

                    args.Add("CancellationToken cancellationToken");
                    w.Line($"/// <summary><c>{operation.Method} {operation.Path}</c></summary>");
                    w.Line($"{operation.HandlerReturnType} {operation.Name}({string.Join(", ", args)});");
                    w.Line();
                }

                w.TrimBlankLine();
                w.Close();
                w.Line();
                EmitValidatingHandlers(w, group.Key, group.ToList());
            }

            if (operations.SelectMany(o => o.ResponseHeaders).Distinct(StringComparer.OrdinalIgnoreCase).ToList() is { Count: > 0 } headers)
            {
                w.Line("/// <summary>");
                w.Line($"/// The response headers the spec documents for <c>{_apiName}</c>, for a handler to set: the generated endpoint opens a scope");
                w.Line("/// around each call and writes what was set onto the response. Outside one (a test calling a handler), setting does nothing.");
                w.Line("/// </summary>");
                w.Open($"public static class {_apiName}Headers");
                w.Line("private static readonly global::System.Threading.AsyncLocal<global::System.Collections.Generic.Dictionary<string, string>?> Current = new();");
                w.Line();
                w.Line("/// <summary>Opens a scope: what handlers set in it, until it's disposed.</summary>");
                w.Line("public static Scope Begin() => new(Current.Value);");
                w.Line();
                w.Line("/// <summary>Sets a response header, by name.</summary>");
                w.Open("public static void Set(string name, string value)");
                w.Open("if (Current.Value is { } values)");
                w.Line("values[name] = value;");
                w.Close();
                w.Close();
                foreach (var header in headers)
                {
                    w.Line();
                    w.Line($"/// <summary>Sets the <c>{header}</c> response header.</summary>");
                    w.Line($"public static void Set{Naming.Pascal(header)}(string value) => Set({Naming.Literal(header)}, value);");
                }

                w.Line();
                w.Open("public sealed class Scope : global::System.IDisposable");
                w.Line("private readonly global::System.Collections.Generic.Dictionary<string, string>? _previous;");
                w.Line("private readonly global::System.Collections.Generic.Dictionary<string, string> _values = new(global::System.StringComparer.OrdinalIgnoreCase);");
                w.Line();
                w.Open("internal Scope(global::System.Collections.Generic.Dictionary<string, string>? previous)");
                w.Line("_previous = previous;");
                w.Line("Current.Value = _values;");
                w.Close();
                w.Line();
                w.Line("/// <summary>The headers set in this scope.</summary>");
                w.Line("public global::System.Collections.Generic.IReadOnlyDictionary<string, string> Values => _values;");
                w.Line();
                w.Line("public void Dispose() => Current.Value = _previous;");
                w.Close();
                w.Close();
                w.Line();
            }

            return w.ToString();
        }

        private static string HandlerInterface(string tag) => $"I{Naming.Pascal(tag)}Handlers";

        /// <summary>
        /// A decorator applying the spec's request validation in front of any implementation of a handler interface, so an
        /// in-process implementation answers an invalid body the way the generated endpoint does. Operations that document
        /// no validation response pass straight through.
        /// </summary>
        private void EmitValidatingHandlers(CodeWriter w, string tag, List<Operation> operations)
        {
            var name = $"Validating{Naming.Pascal(tag)}Handlers";
            var iface = HandlerInterface(tag);
            w.Line($"/// <summary>Applies the spec's request validation in front of any <see cref=\"{iface}\"/>, as the generated endpoints do.</summary>");
            w.Open($"public sealed class {name} : {iface}");
            w.Line($"private readonly {iface} _inner;");
            w.Line();
            w.Line($"public {name}({iface} inner) => _inner = inner;");
            foreach (var operation in operations)
            {
                var args = operation.Parameters.Select(p => $"{p.Type} {p.Identifier}").ToList();
                var call = operation.Parameters.Select(p => p.Identifier).ToList();
                if (operation.Body is not null)
                {
                    args.Add($"{operation.Body.Type} body");
                    call.Add("body");
                }

                args.Add("CancellationToken cancellationToken");
                call.Add("cancellationToken");
                var forward = $"_inner.{operation.Name}({string.Join(", ", call)})";
                var validation = operation.Body?.Validated == true ? operation.Errors.FirstOrDefault(e => e.IsValidation) : null;

                w.Line();
                if (validation is null)
                {
                    w.Line($"public {operation.HandlerReturnType} {operation.Name}({string.Join(", ", args)})");
                    w.Line($"    => {forward};");
                    continue;
                }

                var hasValue = operation.SuccessIsUnion || operation.Successes[0].BodyType is not null;
                var problem = ValidationProblem(validation.BodyType!, "errors");
                var error = $"{operation.ErrorType}.{validation.CaseName}({problem})";
                var failed = hasValue
                    ? $"global::Resultful.Result.Fail<{operation.SuccessType}, {operation.ErrorType}>({error})"
                    : $"global::Resultful.Result.Fail<{operation.ErrorType}>({error})";
                w.Line($"public {operation.HandlerReturnType} {operation.Name}({string.Join(", ", args)})");
                w.Line($"    => {ValidationClass}.Validate(body) is {{ }} errors");
                w.Line($"        ? Task.FromResult({failed})");
                w.Line($"        : {forward};");
            }

            w.Close();
            w.Line();
        }

        // ---------------------------------------------------------------- client

        /// <summary>
        /// An HTTP client that implements the same handler interfaces as the server, so code (and tests) written against
        /// the contract runs in-process or over HTTP unchanged. Every response is checked against the spec: an
        /// undocumented status, the wrong content type or a body that breaks the schema throws ContractViolationException.
        /// </summary>
        private string EmitClient(List<Operation> operations)
        {
            var w = new CodeWriter(_namespace);
            w.Usings(
                "System",
                "System.Collections.Concurrent",
                "System.Collections.Generic",
                "System.Globalization",
                "System.Linq",
                "System.Net.Http",
                "System.Net.Http.Json",
                "System.Text.Json",
                "System.Text.Json.Serialization",
                "System.Threading",
                "System.Threading.Tasks",
                "Resultful");

            w.Line($"/// <summary>Every outcome the spec documents for <c>{_apiName}</c>: each operation with each of its response statuses.</summary>");
            w.Open($"public static class {_apiName}Contract");
            w.Line("public static readonly IReadOnlyList<(string Operation, int Status)> DocumentedOutcomes =");
            w.Line("[");
            foreach (var operation in operations)
            {
                foreach (var response in operation.Successes.Concat(operation.Errors).Where(r => !r.CarriesStatus))
                {
                    w.Line($"    ({Naming.Literal(operation.Id)}, {response.Status.Invariant()}),");
                }
            }

            w.Line("];");
            w.Close();
            w.Line();

            if (!_shared)
            {
                w.Line("/// <summary>A response the spec does not allow: an undocumented status, the wrong content type, or a body that breaks the schema.</summary>");
                w.Open("public sealed class ContractViolationException : Exception");
                w.Line("public ContractViolationException(string operation, int status, string message, Exception? inner = null)");
                w.Open("    : base($\"{operation} returned {status}: {message}\", inner)");
                w.Line("Operation = operation;");
                w.Line("Status = status;");
                w.Close();
                w.Line();
                w.Line("public string Operation { get; }");
                w.Line();
                w.Line("public int Status { get; }");
                w.Close();
                w.Line();
            }

            var interfaces = string.Join(", ", operations.Select(o => HandlerInterface(o.Tag)).Distinct());
            w.Line($"/// <summary>Calls <c>{_apiName}</c> over HTTP through the same interfaces its handlers implement. Relative to the HttpClient's BaseAddress.</summary>");
            w.Open(interfaces.Length == 0 ? $"public sealed partial class {_apiName}Client" : $"public sealed partial class {_apiName}Client : {interfaces}");
            w.Line("private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)");
            w.Line("{");
            w.Line("    NumberHandling = JsonNumberHandling.Strict,");
            w.Line("    AllowOutOfOrderMetadataProperties = true,");
            w.Line("};");
            w.Line();
            w.Line("private readonly HttpClient _http;");
            w.Line("private readonly Action<ContractViolationException>? _onViolation;");
            w.Line("private readonly ConcurrentDictionary<(string Operation, int Status), byte> _observed = new();");
            w.Line("private readonly ConcurrentDictionary<(string Operation, int Status), byte> _refused = new();");
            w.Line();
            w.Line("/// <summary>");
            w.Line("/// A client that throws <see cref=\"ContractViolationException\"/> on any response the spec doesn't allow; or, given");
            w.Line("/// <paramref name=\"onViolation\"/>, a lenient one that reports a violation it can read past (the wrong content type,");
            w.Line("/// a body that breaks the schema) and carries on. What it can't type (an undocumented status, a body that isn't");
            w.Line("/// the schema's JSON) is reported, then still thrown.");
            w.Line("/// </summary>");
            w.Open($"public {_apiName}Client(HttpClient http, Action<ContractViolationException>? onViolation = null)");
            w.Line("_http = http;");
            w.Line("_onViolation = onViolation;");
            w.Close();
            w.Line();
            w.Line("/// <summary>Reports a violation to the lenient handler and carries on, or throws it when there is none.</summary>");
            w.Open("private void Violation(ContractViolationException violation)");
            w.Open("if (_onViolation is null)");
            w.Line("throw violation;");
            w.Close();
            w.Line();
            w.Line("_onViolation(violation);");
            w.Close();
            w.Line();
            w.Line("/// <summary>Reports a violation that can't be read past, then returns it to throw.</summary>");
            w.Open("private ContractViolationException Fatal(ContractViolationException violation)");
            w.Line("_onViolation?.Invoke(violation);");
            w.Line("return violation;");
            w.Close();
            w.Line();
            w.Line("private IReadOnlyDictionary<string, string> _lastHeaders = new Dictionary<string, string>();");
            w.Line();
            w.Line("/// <summary>The headers of the last response (its ETag, say, to send back as If-Match).</summary>");
            w.Line("public IReadOnlyDictionary<string, string> LastHeaders => _lastHeaders;");
            w.Line();
            w.Line("/// <summary>The documented outcomes this client has received, for measuring which ones a test suite reaches.</summary>");
            w.Line("public IReadOnlyCollection<(string Operation, int Status)> Observed => _observed.Keys.ToList();");
            w.Line();
            w.Line("/// <summary>The validation outcomes this client returned itself, refusing a request before sending it (the service's own 400 is the regression suite's to test).</summary>");
            w.Line("public IReadOnlyCollection<(string Operation, int Status)> Refused => _refused.Keys.ToList();");

            foreach (var operation in operations)
            {
                w.Line();
                EmitClientMethod(w, operation);
            }

            w.Line();
            w.Line("/// <summary>A parameter as the spec writes it: an enum by its name in the spec, dates in ISO 8601, true or false in lower case.</summary>");
            w.Line("private static string Text(object value) => value switch");
            w.Line("{");
            w.Line("    Enum => JsonSerializer.Serialize(value, value.GetType(), Json).Trim('\"'),");
            w.Line("    bool b => b ? \"true\" : \"false\",");
            w.Line("    DateTimeOffset d => d.ToString(\"O\", CultureInfo.InvariantCulture),");
            w.Line("    DateTime d => d.ToString(\"O\", CultureInfo.InvariantCulture),");
            w.Line("    DateOnly d => d.ToString(\"yyyy-MM-dd\", CultureInfo.InvariantCulture),");
            w.Line("    _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,");
            w.Line("};");
            w.Line();
            w.Line("private static string Segment(object value) => Uri.EscapeDataString(Text(value));");
            w.Line();
            w.Open("private ContractViolationException Undocumented(string operation, HttpResponseMessage response)");
            w.Line("return Fatal(new(operation, (int)response.StatusCode, \"the spec does not document this status\"));");
            w.Close();
            w.Line();
            w.Open("private static Dictionary<string, string[]>? ValidateEach<T>(IReadOnlyList<T> items, Func<T, Dictionary<string, string[]>?> validate)");
            w.Line("var errors = items.Select((item, index) => (index, errors: validate(item)))");
            w.Line("    .Where(e => e.errors is not null)");
            w.Line("    .SelectMany(e => e.errors!.Select(x => (key: $\"[{e.index}].{x.Key}\", x.Value)))");
            w.Line("    .ToDictionary(e => e.key, e => e.Value);");
            w.Line("return errors.Count == 0 ? null : errors;");
            w.Close();
            w.Line();
            w.Open("private async Task<T> Read<T>(HttpResponseMessage response, string operation, string mediaType, Func<T, Dictionary<string, string[]>?>? validate, CancellationToken cancellationToken)");
            w.Line("var status = (int)response.StatusCode;");
            w.Line("_observed.TryAdd((operation, status), 0);");
            w.Line("var actual = response.Content.Headers.ContentType?.MediaType;");
            w.Open("if (!string.Equals(actual, mediaType, StringComparison.OrdinalIgnoreCase))");
            w.Line("Violation(new ContractViolationException(operation, status, $\"the content type is '{actual}', the spec says '{mediaType}'\"));");
            w.Close();
            w.Line();
            w.Line("T? value;");
            w.Open("try");
            w.Line("value = await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken).ConfigureAwait(false);");
            w.Close();
            w.Open("catch (JsonException e)");
            w.Line("throw Fatal(new ContractViolationException(operation, status, \"the body does not match the schema: \" + e.Message, e));");
            w.Close();
            w.Line();
            w.Open("if (value is null)");
            w.Line("throw Fatal(new ContractViolationException(operation, status, \"the body is null\"));");
            w.Close();
            w.Line();
            w.Open("if (validate?.Invoke(value) is { } errors)");
            w.Line("Violation(new ContractViolationException(operation, status, \"the body breaks the schema: \"");
            w.Line("    + string.Join(\"; \", errors.Select(e => $\"{e.Key}: {string.Join(\" \", e.Value)}\"))));");
            w.Close();
            w.Line();
            w.Line("return value;");
            w.Close();
            w.Line();
            w.Line("/// <summary>A JSON Lines body: every item read and checked against the schema, then yielded.</summary>");
            w.Open("private async Task<IAsyncEnumerable<T>> ReadLines<T>(HttpResponseMessage response, string operation, string mediaType, Func<T, Dictionary<string, string[]>?>? validate, CancellationToken cancellationToken)");
            w.Line("var status = (int)response.StatusCode;");
            w.Line("_observed.TryAdd((operation, status), 0);");
            w.Line("var actual = response.Content.Headers.ContentType?.MediaType;");
            w.Open("if (!string.Equals(actual, mediaType, StringComparison.OrdinalIgnoreCase))");
            w.Line("Violation(new ContractViolationException(operation, status, $\"the content type is '{actual}', the spec says '{mediaType}'\"));");
            w.Close();
            w.Line();
            w.Line("var items = new List<T>();");
            w.Line("var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);");
            w.Line("// JSON Lines: each line an item. Server-sent events: each event's data (its data: lines, joined).");
            w.Line("var lines = mediaType == \"text/event-stream\"");
            w.Line("    ? text.Replace(\"\\r\\n\", \"\\n\").Split(\"\\n\\n\", StringSplitOptions.RemoveEmptyEntries)");
            w.Line("        .Select(e => string.Join(\"\\n\", e.Split('\\n').Where(l => l.StartsWith(\"data:\", StringComparison.Ordinal)).Select(l => l.Substring(5).TrimStart(' '))))");
            w.Line("        .Where(d => d.Length > 0)");
            w.Line("        .ToArray()");
            w.Line("    : text.Split('\\n', StringSplitOptions.RemoveEmptyEntries);");
            w.Open("foreach (var (line, index) in lines.Select((l, i) => (l, i)))");
            w.Line("T? item;");
            w.Open("try");
            w.Line("item = typeof(T) == typeof(string) && mediaType == \"text/event-stream\" ? (T)(object)line : JsonSerializer.Deserialize<T>(line, Json);");
            w.Close();
            w.Open("catch (JsonException e)");
            w.Line("throw Fatal(new ContractViolationException(operation, status, $\"item {index + 1} does not match the schema: {e.Message}\", e));");
            w.Close();
            w.Line();
            w.Open("if (item is null)");
            w.Line("throw Fatal(new ContractViolationException(operation, status, $\"item {index + 1} is null\"));");
            w.Close();
            w.Line();
            w.Open("if (validate?.Invoke(item) is { } errors)");
            w.Line("Violation(new ContractViolationException(operation, status, $\"item {index + 1} breaks the schema: \" + string.Join(\"; \", errors.Select(e => $\"{e.Key}: {string.Join(\" \", e.Value)}\"))));");
            w.Close();
            w.Line();
            w.Line("items.Add(item);");
            w.Close();
            w.Line();
            w.Line("return items.ToAsyncEnumerable();");
            w.Close();
            w.Line();
            w.Open("private void Observe(string operation, HttpResponseMessage response)");
            w.Line("_observed.TryAdd((operation, (int)response.StatusCode), 0);");
            w.Close();
            w.Close();
            return w.ToString();
        }

        private string TypeOfValidationProblem => Mapped.TryGetValue(ValidationSchema, out var mapped) ? mapped : Naming.Pascal(ValidationSchema);

        /// <summary>A validation problem of these errors (a Dictionary of string[]): ASP.NET Core's, or the contract's own record.</summary>
        private string ValidationProblem(string type, string errors) => _ownProblems
            ? $"new {type} {{ Title = \"One or more validation errors occurred.\", Status = 400, Errors = global::System.Linq.Enumerable.ToDictionary({errors}, e => e.Key, e => (global::System.Collections.Generic.IReadOnlyList<string>)e.Value) }}"
            : $"new {type}({errors}) {{ Title = \"One or more validation errors occurred.\", Status = 400 }}";

        /// <summary>
        /// The request checked before it's sent, as the server would check it: parameter bounds, x-dependencies (with
        /// Matchmark.Rules) and the body's schema and rules. A bad request never leaves: it is the spec's validation 400
        /// when the operation documents one, built here without a round trip, and otherwise an ArgumentException.
        /// </summary>
        private void EmitClientChecks(CodeWriter w, Operation operation)
        {
            var bounds = ParameterChecks(operation).ToList();
            var dependencies = operation.Dependencies.Count > 0 && _hasRules;
            var body = operation.Body is { Validated: true };
            if (bounds.Count == 0 && !dependencies && !body)
            {
                return;
            }

            w.Line("var __invalid = new Dictionary<string, string[]>();");
            foreach (var check in bounds)
            {
                w.Line(check);
            }

            if (dependencies)
            {
                var given = operation.Parameters.Select(p => $"[{Naming.Literal(p.Name)}] = {ValidationClass}.Text({p.Identifier})");
                w.Line($"foreach (var __error in {ValidationClass}.Broken{operation.Name}Dependencies(new Dictionary<string, string?> {{ {string.Join(", ", given)} }}) ?? new()) __invalid[__error.Key] = __error.Value;");
            }

            if (body)
            {
                w.Line($"foreach (var __error in {ValidationClass}.Validate(body) ?? new()) __invalid[__error.Key] = __error.Value;");
            }

            w.Open("if (__invalid.Count > 0)");
            var validation = operation.Errors.FirstOrDefault(e => e.Status == 400 && (e.IsValidation || e.BodyType == TypeOfValidationProblem));
            if (validation is null)
            {
                w.Line($"throw new ArgumentException({Naming.Literal(operation.Id + ": the request breaks the spec: ")} + string.Join(\"; \", __invalid.Select(e => $\"{{e.Key}}: {{string.Join(\" \", e.Value)}}\")));");
            }
            else
            {
                var hasValue = operation.SuccessIsUnion || operation.Successes[0].BodyType is not null;
                var problem = ValidationProblem(validation.BodyType!, "__invalid");
                var error = $"{operation.ErrorType}.{validation.CaseName}({problem})";
                w.Line($"_refused.TryAdd(({Naming.Literal(operation.Id)}, 400), 0);");
                w.Line(hasValue
                    ? $"return global::Resultful.Result.Fail<{operation.SuccessType}, {operation.ErrorType}>({error});"
                    : $"return global::Resultful.Result.Fail<{operation.ErrorType}>({error});");
            }

            w.Close();
            w.Line();
        }

        private void EmitClientMethod(CodeWriter w, Operation operation)
        {
            var args = operation.Parameters.Select(p => $"{p.Type} {p.Identifier}").ToList();
            if (operation.Body is not null)
            {
                args.Add($"{operation.Body.Type} body");
            }

            args.Add("CancellationToken cancellationToken");
            w.Line($"/// <summary><c>{operation.Method} {operation.Path}</c></summary>");
            w.Open($"public async {operation.HandlerReturnType} {operation.Name}({string.Join(", ", args)})");
            EmitClientChecks(w, operation);

            // The path, relative so a BaseAddress with a path prefix works: "/pets/{petId}" -> "pets/" + Segment(petId)
            var pieces = new List<string>();
            var path = operation.Path.TrimStart('/');
            var index = 0;
            while (index < path.Length)
            {
                var open = path.IndexOf('{', index);
                if (open < 0)
                {
                    pieces.Add(Naming.Literal(path.Substring(index)));
                    break;
                }

                if (open > index)
                {
                    pieces.Add(Naming.Literal(path.Substring(index, open - index)));
                }

                var close = path.IndexOf('}', open);
                var name = path.Substring(open + 1, close - open - 1);
                var parameter = operation.Parameters.First(p => p.Name == name);
                pieces.Add($"Segment({parameter.Identifier})");
                index = close + 1;
            }

            w.Line($"var __uri = {(pieces.Count == 0 ? "\"\"" : string.Join(" + ", pieces))};");
            var query = operation.Parameters.Where(p => p.Attribute == "FromQuery").ToList();
            if (query.Count > 0)
            {
                w.Line("var __query = new List<string>();");
                foreach (var p in query.Where(p => p.Deep))
                {
                    var local = $"__d_{p.Identifier.TrimStart('@')}";
                    var fields = p.Whole ? $"{ValidationClass}.FormFields({local})" : $"{ValidationClass}.QueryFields({Naming.Literal(p.Name)}, {local})";
                    w.Line($"if ((object?){p.Identifier} is {{ }} {local}) foreach (var __f in {fields}) __query.Add(Uri.EscapeDataString(__f.Key) + \"=\" + Uri.EscapeDataString(__f.Value));");
                }

                foreach (var p in query.Where(p => !p.Deep))
                {
                    w.Line(p.Type.EndsWith("?", StringComparison.Ordinal)
                        ? $"if ({p.Identifier} is {{ }} __{p.Identifier.TrimStart('@')}) __query.Add({Naming.Literal(p.Name + "=")} + Segment(__{p.Identifier.TrimStart('@')}));"
                        : $"__query.Add({Naming.Literal(p.Name + "=")} + Segment({p.Identifier}));");
                }

                w.Line("if (__query.Count > 0) __uri += \"?\" + string.Join(\"&\", __query);");
            }

            w.Line($"using var __request = new HttpRequestMessage(new HttpMethod({Naming.Literal(operation.Method)}), __uri);");
            foreach (var p in operation.Parameters.Where(p => p.Attribute == "FromHeader"))
            {
                w.Line($"if ((object?){p.Identifier} is {{ }} __h_{p.Identifier.TrimStart('@')}) __request.Headers.TryAddWithoutValidation({Naming.Literal(p.Name)}, Text(__h_{p.Identifier.TrimStart('@')}));");
            }

            if (operation.Body is { Form: true })
            {
                w.Line($"__request.Content = new FormUrlEncodedContent({ValidationClass}.FormFields(body));");
            }
            else if (operation.Body is { Text: true } text)
            {
                w.Line($"__request.Content = new StringContent(body, global::System.Text.Encoding.UTF8, {Naming.Literal(text.MediaType)});");
            }
            else if (operation.Body is not null)
            {
                w.Line("__request.Content = JsonContent.Create(body, options: Json);");
            }

            w.Line("using var __response = await _http.SendAsync(__request, cancellationToken).ConfigureAwait(false);");
            w.Line("_lastHeaders = __response.Headers.Concat(__response.Content.Headers).GroupBy(h => h.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => string.Join(\", \", g.First().Value), StringComparer.OrdinalIgnoreCase);");
            w.Open("switch ((int)__response.StatusCode)");

            foreach (var response in operation.Successes.Concat(operation.Errors).OrderBy(r => r.Order))
            {
                var value = ClientRead(operation, response);
                EmitCase(w, operation, response, Outcome(operation, response, value, "(int)__response.StatusCode"), value is null);
            }

            if (!operation.Errors.Any(e => e.IsDefault))
            {
                w.Line("default:");
                w.Line($"    throw Undocumented({Naming.Literal(operation.Id)}, __response);");
            }
            w.Close();
            w.Close();
        }

        /// <summary>
        /// The handler's return value for a documented response, given an expression for its body (null when it has none).
        /// Empty for an operation whose handler returns a plain Task. Shared by the client and the fake.
        /// </summary>
        private static string Outcome(Operation operation, Response response, string? value, string status = "0")
        {
            var hasErrors = operation.Errors.Count > 0;
            var successHasValue = operation.SuccessIsUnion || operation.Successes[0].BodyType is not null;
            if (operation.Errors.Contains(response))
            {
                var arguments = response.CarriesStatus ? (value is null ? status : $"{status}, {value}") : value ?? string.Empty;
                var error = $"{operation.ErrorType}.{response.CaseName}({arguments})";
                return successHasValue
                    ? $"global::Resultful.Result.Fail<{operation.SuccessType}, {operation.ErrorType}>({error})"
                    : $"global::Resultful.Result.Fail<{operation.ErrorType}>({error})";
            }

            if (operation.SuccessIsUnion)
            {
                var union = $"{operation.SuccessType}.{response.CaseName}({value ?? string.Empty})";
                return hasErrors ? $"global::Resultful.Result.Ok<{operation.SuccessType}, {operation.ErrorType}>({union})" : union;
            }

            if (successHasValue)
            {
                return hasErrors ? $"global::Resultful.Result.Ok<{response.BodyType}, {operation.ErrorType}>({value})" : value!;
            }

            return hasErrors ? $"global::Resultful.Result.Ok<{operation.ErrorType}>()" : string.Empty;
        }

        private static void EmitCase(CodeWriter w, Operation operation, Response response, string result, bool observeHere)
        {
            w.Line(response.IsDefault ? "default:" : $"case {response.Pattern}:");
            if (observeHere)
            {
                // No body to read (and so nothing else records the outcome).
                w.Line($"    Observe({Naming.Literal(operation.Id)}, __response);");
            }

            w.Line(result.Length == 0 ? "    return;" : $"    return {result};");
        }

        /// <summary>The expression reading this response's body, checked against the spec; null when it has none.</summary>
        private string? ClientRead(Operation operation, Response response)
        {
            if (response.BodyType is null)
            {
                return null;
            }

            if (response.IsRedirect)
            {
                // The client must not follow redirects itself (HttpClientHandler.AllowAutoRedirect = false) to see this.
                return "(__response.Headers.Location?.ToString() ?? string.Empty)";
            }

            var validate = response.SchemaName is null
                ? "null"
                : response.IsList
                    ? $"items => ValidateEach(items, {ValidationClass}.Validate)"
                    : $"{ValidationClass}.Validate";
            if (response.IsStream)
            {
                return $"await ReadLines<{response.ItemType}>(__response, {Naming.Literal(operation.Id)}, {Naming.Literal(response.MediaType!)}, {validate}, cancellationToken).ConfigureAwait(false)";
            }

            return $"await Read<{response.BodyType}>(__response, {Naming.Literal(operation.Id)}, {Naming.Literal(response.MediaType!)}, {validate}, cancellationToken).ConfigureAwait(false)";
        }

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

        // ---------------------------------------------------------------- fake

        /// <summary>
        /// A fake implementation of the handler interfaces. Every operation answers with its first documented success,
        /// using the spec's example or a generated sample, until scripted: per operation (<c>On{Operation}</c>,
        /// <c>Respond</c>) or with a scenario of expected calls (<c>UseScenario</c>). Behind the generated endpoints it is
        /// a fake server that needs no code. Requires Matchmark.Fakes.
        /// </summary>
        private string EmitFake(List<Operation> operations)
        {
            var w = new CodeWriter(_namespace);
            w.Usings("System", "System.Collections.Concurrent", "System.Collections.Generic", "System.Linq", "System.Text.Json",
                "System.Text.Json.Nodes", "System.Text.Json.Serialization", "System.Threading", "System.Threading.Tasks",
                "Resultful", "Matchmark.Fakes");

            var interfaces = string.Join(", ", operations.Select(o => HandlerInterface(o.Tag)).Distinct());
            var name = _apiName + "Fake";
            w.Line("/// <summary>");
            w.Line($"/// A fake <c>{_apiName}</c>. Each operation answers with its first documented success (the spec's example, or a");
            w.Line("/// sample built to pass the schema) until scripted with <c>On{Operation}</c>, <see cref=\"Respond\"/> or");
            w.Line("/// <see cref=\"UseScenario\"/>. Only documented outcomes, with bodies that pass the schema, can be scripted.");
            w.Line("/// </summary>");
            w.Open(interfaces.Length == 0 ? $"public sealed class {name}" : $"public sealed class {name} : {interfaces}");
            w.Line("private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)");
            w.Line("{");
            w.Line("    NumberHandling = JsonNumberHandling.Strict,");
            w.Line("    AllowOutOfOrderMetadataProperties = true,");
            w.Line("};");
            w.Line();
            w.Line("/// <summary>Each operation's parameters, by name; <c>body</c> is the request body. What a scenario may expect.</summary>");
            w.Line("public static readonly IReadOnlyDictionary<string, string[]> Parameters = new Dictionary<string, string[]>");
            w.Line("{");
            foreach (var operation in operations)
            {
                var names = operation.Parameters.Select(p => p.Name).ToList();
                if (operation.Body is not null)
                {
                    names.Add("body");
                }

                w.Line($"    [{Naming.Literal(operation.Id)}] = [{string.Join(", ", names.Select(Naming.Literal))}],");
            }

            w.Line("};");
            w.Line();
            w.Line("private readonly ConcurrentQueue<FakeCall> _calls = new();");
            w.Line("private readonly FakeCallLog _log;");
            w.Line("private ScenarioRunner? _scenario;");
            w.Line();
            w.Line("/// <summary>The rules set up with <see cref=\"Given\"/>.</summary>");
            w.Line("internal readonly FakeRuleSet Rules = new();");
            foreach (var operation in operations)
            {
                w.Line($"private {Responder(operation)} {Field(operation)} = null!;");
            }

            w.Line();
            w.Open($"public {name}()");
            w.Line("_log = new FakeCallLog(() => Calls);");
            w.Line("Reset();");
            w.Close();
            w.Line();
            w.Line($"/// <summary>Sets up answers, fluently: <c>Given.GetPet(petId: 7).Returns.NotFound()</c>.</summary>");
            w.Line($"public {_apiName}FakeGiven Given => new(this);");
            w.Line();
            w.Line($"/// <summary>Asserts on the calls received, fluently: <c>Should().HaveReceived.GetPet(petId: 7).Once()</c>.</summary>");
            w.Line($"public {_apiName}FakeAssertions Should() => new(this, _log);");
            w.Line();
            w.Line("/// <summary>Every call so far, oldest first.</summary>");
            w.Line("public IReadOnlyList<FakeCall> Calls => _calls.ToList();");
            w.Line();
            w.Line("/// <summary>Forgets the calls, rules and any scenario, and returns every operation to its default answer.</summary>");
            w.Open("public void Reset()");
            w.Line("_calls.Clear();");
            w.Line("_scenario = null;");
            w.Line("Rules.Clear();");
            foreach (var operation in operations)
            {
                w.Line($"Respond({Naming.Literal(operation.Id)}, {operation.Successes[0].Status.Invariant()});");
            }

            w.Close();
            w.Line();
            w.Line("/// <summary>Answers calls from this scenario from now on, and checks each call against it. Throws if the scenario breaks the spec.</summary>");
            w.Open($"public {name} UseScenario(Scenario scenario)");
            w.Line("var problems = Check(scenario);");
            w.Open("if (problems.Count > 0)");
            w.Line("throw new ArgumentException(\"The scenario breaks the spec:\\n\" + string.Join(\"\\n\", problems), nameof(scenario));");
            w.Close();
            w.Line();
            w.Line("_scenario = new ScenarioRunner(scenario);");
            w.Line("return this;");
            w.Close();
            w.Line();
            w.Line("/// <summary>Whether the scenario's steps were all called, in order if it requires that, and nothing else was.</summary>");
            w.Line("public ScenarioReport Verify() => _scenario?.Verify() ?? new ScenarioReport(true, [], Calls);");
            w.Line();
            w.Line("/// <summary>Everything in <paramref name=\"scenario\"/> the spec doesn't allow: unknown operations or parameters, undocumented statuses, bodies that break the schema.</summary>");
            w.Open("public static IReadOnlyList<string> Check(Scenario scenario)");
            w.Line("var problems = new List<string>();");
            w.Open("for (var i = 0; i < scenario.Steps.Count; i++)");
            w.Line("var step = scenario.Steps[i];");
            w.Line("var where = $\"step {i + 1} ({step.Operation} → {step.Status})\";");
            w.Open("if (!Parameters.TryGetValue(step.Operation, out var parameters))");
            w.Line("problems.Add($\"{where}: the spec has no operation '{step.Operation}'\");");
            w.Line("continue;");
            w.Close();
            w.Line();
            w.Open("if (step.Times < 1)");
            w.Line("problems.Add($\"{where}: times must be at least 1\");");
            w.Close();
            w.Line();
            w.Open("foreach (var key in step.Expect?.Select(e => e.Key) ?? [])");
            w.Open("if (!parameters.Contains(key))");
            w.Line("problems.Add($\"{where}: '{key}' is not a parameter; expected one of {string.Join(\", \", parameters)}\");");
            w.Close();
            w.Close();
            w.Line();
            w.Open("try");
            w.Open("if (!TryAnswer(step.Operation, step.Status, step.Body?.ToJsonString(), out _))");
            w.Line("problems.Add($\"{where}: the spec documents no {step.Status} response for '{step.Operation}'\");");
            w.Close();
            w.Close();
            w.Open("catch (Exception e) when (e is ArgumentException or JsonException)");
            w.Line("problems.Add($\"{where}: {e.Message}\");");
            w.Close();
            w.Close();
            w.Line();
            w.Line("return problems;");
            w.Close();

            foreach (var operation in operations)
            {
                var field = Field(operation);
                var args = operation.Parameters.Select(p => $"{p.Type} {p.Identifier}").ToList();
                var names = operation.Parameters.Select(p => p.Identifier).ToList();
                var types = operation.Parameters.Select(p => p.Type).ToList();
                var recorded = operation.Parameters.Select(p => $"[{Naming.Literal(p.Name)}] = {p.Identifier}").ToList();
                if (operation.Body is not null)
                {
                    args.Add($"{operation.Body.Type} body");
                    names.Add("body");
                    types.Add(operation.Body.Type);
                    recorded.Add("[\"body\"] = body");
                }

                w.Line();
                w.Open($"public {operation.HandlerReturnType} {operation.Name}({string.Join(", ", args.Append("CancellationToken cancellationToken"))})");
                w.Line($"var call = new FakeCall({Naming.Literal(operation.Id)}, new Dictionary<string, object?> {{ {string.Join(", ", recorded)} }});");
                w.Line("_calls.Enqueue(call);");
                w.Open("if (_scenario is { } scenario)");
                w.Line("var (status, json) = scenario.Next(call, Arguments(call));");
                w.Line($"var answer = Answer({Naming.Literal(operation.Id)}, status, json);");
                w.Line($"scenario.Answered(json ?? SampleJson({Naming.Literal(operation.Id)}, status));");
                w.Line(operation.HandlerResultType is { } r
                    ? $"return Task.FromResult(({r})answer!);"
                    : "return Task.CompletedTask;");
                w.Close();
                w.Line();
                w.Open("if (Rules.Find(call) is { } rule)");
                if (operation.HandlerResultType is { } rr)
                {
                    w.Line($"return Task.FromResult(({rr})rule.Next()!);");
                }
                else
                {
                    w.Line("rule.Next();");
                    w.Line("return Task.CompletedTask;");
                }

                w.Close();
                w.Line();
                w.Open("if (Rules.NothingElse)");
                w.Line("throw new FakeExpectationException($\"{FakeCallMatcher.Describe(call)} matched no Given, and the fake was told to expect nothing else\");");
                w.Close();
                w.Line();
                w.Line($"return {field}({string.Join(", ", names.Append("cancellationToken"))});");
                w.Close();
                w.Line();
                w.Line($"/// <summary>Answers <c>{operation.Id}</c> with this function from now on.</summary>");
                w.Open($"public {name} On{operation.Name}({Responder(operation)} respond)");
                w.Line($"{field} = respond;");
                w.Line("return this;");
                w.Close();

                if (operation.HandlerResultType is { } result)
                {
                    var sync = $"Func<{string.Join(", ", types.Append(result))}>";
                    var lambdaArgs = names.Count == 0 ? "_" : $"({string.Join(", ", names.Append("_"))})";
                    w.Line();
                    w.Open($"public {name} On{operation.Name}({sync} respond)");
                    w.Line($"{field} = {lambdaArgs} => Task.FromResult(respond({string.Join(", ", names)}));");
                    w.Line("return this;");
                    w.Close();
                }
            }

            w.Line();
            w.Line("/// <summary>");
            w.Line("/// Answers every later call to <paramref name=\"operation\"/> (its operationId) with the documented response for");
            w.Line("/// <paramref name=\"status\"/>: <paramref name=\"json\"/> as its body, or the default sample. False if the spec");
            w.Line("/// documents no such response; throws if the body breaks the schema.");
            w.Line("/// </summary>");
            w.Open("public bool Respond(string operation, int status, string? json = null)");
            w.Open("if (!TryAnswer(operation, status, json, out _))");
            w.Line("return false;");
            w.Close();
            w.Line();
            w.Open("switch (operation)");
            foreach (var operation in operations)
            {
                var discards = string.Join(", ", Enumerable.Repeat("_", operation.Parameters.Count + (operation.Body is null ? 0 : 1) + 1));
                var lambdaArgs = discards == "_" ? "_" : $"({discards})";
                w.Line($"case {Naming.Literal(operation.Id)}:");
                w.Line(operation.HandlerResultType is { } result
                    ? $"    {Field(operation)} = {lambdaArgs} => Task.FromResult(({result})Answer(operation, status, json)!);"
                    : $"    {Field(operation)} = {lambdaArgs} => Task.CompletedTask;");
                w.Line("    return true;");
            }

            w.Line("default:");
            w.Line("    return false;");
            w.Close();
            w.Close();
            w.Line();
            w.Line("/// <summary>The body the fake answers with when a scenario gives none: the spec's example, or a sample.</summary>");
            w.Open("internal static string? SampleJson(string operation, int status) => (operation, status) switch");
            foreach (var operation in operations)
            {
                foreach (var response in operation.Successes.Concat(operation.Errors).Where(r => r.BodyType is not null && r.SampleJson is not null).OrderBy(r => r.Order))
                {
                    w.Line($"({Naming.Literal(operation.Id)}, {response.Pattern}) => {Naming.Literal(response.SampleJson!)},");
                }
            }

            w.Line("_ => null,");
            w.Close(suffix: ";");
            w.Line();
            w.Line("internal static object? Answer(string operation, int status, string? json)");
            w.Line("    => TryAnswer(operation, status, json, out var answer) ? answer : throw new ArgumentException($\"The spec documents no {status} response for '{operation}'.\");");
            w.Line();
            w.Line("/// <summary>The handler's return value for a documented response, built fresh; null for a plain Task.</summary>");
            w.Open("internal static bool TryAnswer(string operation, int status, string? json, out object? answer)");
            w.Open("switch ((operation, status))");
            foreach (var operation in operations)
            {
                // The default response answers any status the others don't, so it comes last.
                foreach (var response in operation.Successes.Concat(operation.Errors).OrderBy(r => r.Order))
                {
                    w.Line($"case ({Naming.Literal(operation.Id)}, {response.Pattern}):");
                    string? value = null;
                    if (response.BodyType is not null)
                    {
                        var validate = response.SchemaName is null
                            ? "null"
                            : response.IsList ? $"items => ValidateEach(items, {ValidationClass}.Validate)" : $"{ValidationClass}.Validate";
                        value = response.IsStream
                            ? $"Body<global::System.Collections.Generic.List<{response.ItemType}>>(json ?? {Naming.Literal(response.SampleJson ?? "[]")}, null).ToAsyncEnumerable()"
                            : $"Body<{response.BodyType}>(json ?? {Naming.Literal(response.SampleJson ?? "null")}, {validate})";
                    }

                    var outcome = Outcome(operation, response, value, "status");
                    w.Line(outcome.Length == 0 ? "    answer = null;" : $"    answer = {outcome};");
                    w.Line("    return true;");
                }
            }

            w.Line("default:");
            w.Line("    answer = null;");
            w.Line("    return false;");
            w.Close();
            w.Close();
            w.Line();
            w.Open("private static T Body<T>(string json, Func<T, Dictionary<string, string[]>?>? validate)");
            w.Line("var value = JsonSerializer.Deserialize<T>(json, Json) ?? throw new ArgumentException(\"The body is null.\");");
            w.Open("if (validate?.Invoke(value) is { } errors)");
            w.Line("throw new ArgumentException(\"The body breaks the schema: \" + string.Join(\"; \", errors.Select(e => $\"{e.Key}: {string.Join(\" \", e.Value)}\")));");
            w.Close();
            w.Line();
            w.Line("return value;");
            w.Close();
            w.Line();
            w.Open("private static Dictionary<string, string[]>? ValidateEach<T>(IReadOnlyList<T> items, Func<T, Dictionary<string, string[]>?> validate)");
            w.Line("var errors = items.Select((item, index) => (index, errors: validate(item)))");
            w.Line("    .Where(e => e.errors is not null)");
            w.Line("    .SelectMany(e => e.errors!.Select(x => (key: $\"[{e.index}].{x.Key}\", x.Value)))");
            w.Line("    .ToDictionary(e => e.key, e => e.Value);");
            w.Line("return errors.Count == 0 ? null : errors;");
            w.Close();
            w.Line();
            w.Line("internal static string? Serialize(object? body) => body is null ? null : JsonSerializer.Serialize(body, body.GetType(), Json);");
            w.Line();
            w.Open("private static JsonObject Arguments(FakeCall call)");
            w.Line("var json = new JsonObject();");
            w.Open("foreach (var (key, value) in call.Arguments)");
            w.Line("json[key] = value is null ? null : JsonSerializer.SerializeToNode(value, value.GetType(), Json);");
            w.Close();
            w.Line();
            w.Line("return json;");
            w.Close();
            w.Close();
            w.Line();

            EmitScenarioBuilder(w, operations, name);
            w.Line();
            EmitFluentFake(w, operations, name);
            return w.ToString();
        }

        /// <summary>A typed way to write a scenario: one method per operation, checked against the spec on Build.</summary>
        private void EmitScenarioBuilder(CodeWriter w, List<Operation> operations, string fake)
        {
            var name = _apiName + "Scenario";
            w.Line($"/// <summary>Builds a <see cref=\"Scenario\"/> for <see cref=\"{fake}\"/> in code: each method adds one expected call.</summary>");
            w.Open($"public sealed class {name}");
            w.Line("private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { NumberHandling = JsonNumberHandling.Strict };");
            w.Line("private readonly List<ScenarioStep> _steps = [];");
            w.Line("private readonly ScenarioOrder _order;");
            w.Line();
            w.Line($"private {name}(ScenarioOrder order) => _order = order;");
            w.Line();
            w.Line("/// <summary>Calls must come in the order the steps are added.</summary>");
            w.Line($"public static {name} InOrder() => new(ScenarioOrder.InOrder);");
            w.Line();
            w.Line("/// <summary>Calls may come in any order.</summary>");
            w.Line($"public static {name} AnyOrder() => new(ScenarioOrder.AnyOrder);");
            foreach (var operation in operations)
            {
                var parameters = operation.Parameters.Select(p => p.Name).ToList();
                if (operation.Body is not null)
                {
                    parameters.Add("body");
                }

                var statuses = string.Join(", ", operation.Successes.Concat(operation.Errors).Select(r => r.Status.Invariant()));
                w.Line();
                w.Line("/// <summary>");
                w.Line($"/// Expects <c>{operation.Method} {operation.Path}</c>. <paramref name=\"expect\"/> is a subset of its arguments");
                w.Line($"/// ({(parameters.Count == 0 ? "none" : string.Join(", ", parameters))}); <paramref name=\"status\"/> is one of {statuses};");
                w.Line("/// <paramref name=\"body\"/> replaces the spec's example.");
                w.Line("/// </summary>");
                w.Line($"public {name} {operation.Name}(object? expect = null, int status = {operation.Successes[0].Status.Invariant()}, object? body = null, int times = 1)");
                w.Line($"    => Add({Naming.Literal(operation.Id)}, expect, status, body, times);");
            }

            w.Line();
            w.Line("/// <summary>The scenario, checked against the spec. Throws listing every problem.</summary>");
            w.Open("public Scenario Build()");
            w.Line("var scenario = new Scenario { Order = _order, Steps = _steps.ToList() };");
            w.Line($"var problems = {fake}.Check(scenario);");
            w.Line("return problems.Count == 0 ? scenario : throw new ArgumentException(\"The scenario breaks the spec:\\n\" + string.Join(\"\\n\", problems));");
            w.Close();
            w.Line();
            w.Open($"private {name} Add(string operation, object? expect, int status, object? body, int times)");
            w.Line("_steps.Add(new ScenarioStep");
            w.Line("{");
            w.Line("    Operation = operation,");
            w.Line("    Expect = expect is null ? null : JsonSerializer.SerializeToNode(expect, expect.GetType(), Json)!.AsObject(),");
            w.Line("    Status = status,");
            w.Line("    Body = body is null ? null : JsonSerializer.SerializeToNode(body, body.GetType(), Json),");
            w.Line("    Times = times,");
            w.Line("});");
            w.Line("return this;");
            w.Close();
            w.Close();
        }

        /// <summary>
        /// The fluent API over the fake, one typed method per operation: matchers (by argument equality or a predicate),
        /// Given rules whose Returns methods are named after the spec's responses, and HaveReceived assertions.
        /// </summary>
        private void EmitFluentFake(CodeWriter w, List<Operation> operations, string fake)
        {
            var calls = _apiName + "Calls";
            var given = _apiName + "FakeGiven";
            var received = _apiName + "Received";
            var assertions = _apiName + "FakeAssertions";

            // Matchers.
            w.Line($"/// <summary>Describes calls to <c>{_apiName}</c>: by argument equality (null matches anything) or by a predicate.</summary>");
            w.Open($"public sealed class {calls}");
            foreach (var operation in operations)
            {
                var args = Arguments(operation);
                var id = Naming.Literal(operation.Id);
                w.Line();
                w.Line($"/// <summary><c>{operation.Id}</c> with these arguments; leave one out (or null) to match any value.</summary>");
                w.Line($"public FakeCallMatcher {operation.Name}({string.Join(", ", args.Select(a => $"{Optional(a.Type)} {a.Identifier} = null"))})");
                var describe = args.Count == 0
                    ? "System.Array.Empty<(string, object?)>()"
                    : $"new (string, object?)[] {{ {string.Join(", ", args.Select(a => $"({Naming.Literal(a.Name)}, {a.Identifier})"))} }}.Where(a => a.Item2 is not null)";
                var test = args.Count == 0
                    ? "_ => true"
                    : "call => " + string.Join(" && ", args.Select(a => $"({a.Identifier} is null || Equals(call.Arguments[{Naming.Literal(a.Name)}], {a.Identifier}))"));
                w.Line($"    => new({id}, FakeCallMatcher.Describe({id}, {describe}), {test});");
                if (args.Count > 0)
                {
                    var func = $"Func<{string.Join(", ", args.Select(a => a.Type).Append("bool"))}>";
                    var cast = string.Join(", ", args.Select(a => $"({a.Type})call.Arguments[{Naming.Literal(a.Name)}]!"));
                    w.Line();
                    w.Line($"/// <summary><c>{operation.Id}</c> whose arguments ({string.Join(", ", args.Select(a => a.Name))}) satisfy <paramref name=\"where\"/>.</summary>");
                    w.Line($"public FakeCallMatcher {operation.Name}({func} where)");
                    w.Line($"    => new({id}, {Naming.Literal(operation.Id + "(where …)")}, call => where({cast}));");
                }
            }

            w.Close();
            w.Line();

            // Given.
            w.Line($"/// <summary>Sets up how the fake answers. The latest matching rule wins.</summary>");
            w.Open($"public sealed class {given}");
            w.Line($"private readonly {fake} _fake;");
            w.Line();
            w.Line($"internal {given}({fake} fake) => _fake = fake;");
            w.Line();
            w.Line("/// <summary>From now on, a call no rule matches fails instead of getting the default answer.</summary>");
            w.Line("public void NothingElse() => _fake.Rules.NothingElse = true;");
            foreach (var operation in operations)
            {
                var args = Arguments(operation);
                var rule = operation.Name + "Rule";
                w.Line();
                w.Line($"public {rule} {operation.Name}({string.Join(", ", args.Select(a => $"{Optional(a.Type)} {a.Identifier} = null"))})");
                w.Line($"    => new(_fake.Rules.Add(new {calls}().{operation.Name}({string.Join(", ", args.Select(a => a.Identifier))})));");
                if (args.Count > 0)
                {
                    w.Line();
                    w.Line($"public {rule} {operation.Name}(Func<{string.Join(", ", args.Select(a => a.Type).Append("bool"))}> where)");
                    w.Line($"    => new(_fake.Rules.Add(new {calls}().{operation.Name}(where)));");
                }
            }

            w.Close();
            w.Line();

            // Per operation: the rule and its answers, named after the spec's responses.
            foreach (var operation in operations)
            {
                var rule = operation.Name + "Rule";
                var answers = operation.Name + "Answers";
                w.Line($"/// <summary>A <c>Given</c> for <c>{operation.Id}</c>. <c>Returns</c> its first answer; <c>Then</c> the next.</summary>");
                w.Open($"public sealed class {rule}");
                w.Line("private readonly FakeRule _rule;");
                w.Line();
                w.Line($"internal {rule}(FakeRule rule) => _rule = rule;");
                w.Line();
                w.Line($"public {answers} Returns => new(_rule, this);");
                w.Line();
                w.Line($"public {answers} Then => new(_rule, this);");
                w.Close();
                w.Line();
                w.Line($"/// <summary>The documented answers to <c>{operation.Id}</c>. Without a body, the spec's example or a valid sample is used.</summary>");
                w.Open($"public sealed class {answers}");
                w.Line("private readonly FakeRule _rule;");
                w.Line($"private readonly {rule} _owner;");
                w.Line();
                w.Line($"internal {answers}(FakeRule rule, {rule} owner) => (_rule, _owner) = (rule, owner);");
                foreach (var response in operation.Successes.Concat(operation.Errors))
                {
                    w.Line();
                    w.Line($"/// <summary>{response.Status.Invariant()} {response.CaseName}. Throws now if <paramref name=\"body\"/> breaks the schema.</summary>");
                    // A range or default case answers with the status it's given.
                    var parameters = new List<string>();
                    if (response.CarriesStatus)
                    {
                        parameters.Add("int status");
                    }

                    if (response.BodyType is not null)
                    {
                        parameters.Add($"{Optional(response.BodyType)} body = null");
                    }

                    var status = response.CarriesStatus ? "status" : response.Status.Invariant();
                    w.Open($"public {rule} {response.CaseName}({string.Join(", ", parameters)})");
                    w.Line(response.BodyType is null ? "string? json = null;" : $"var json = {fake}.Serialize(body);");
                    w.Line($"_ = {fake}.Answer({Naming.Literal(operation.Id)}, {status}, json);");
                    w.Line($"_rule.Add(() => {fake}.Answer({Naming.Literal(operation.Id)}, {status}, json));");
                    w.Line("return _owner;");
                    w.Close();
                }

                w.Close();
                w.Line();
            }

            // HaveReceived.
            w.Line("/// <summary>What the fake should have received: pick the call, then how many times.</summary>");
            w.Open($"public sealed class {received}");
            w.Line("private readonly FakeCallLog _log;");
            w.Line();
            w.Line($"internal {received}(FakeCallLog log) => _log = log;");
            foreach (var operation in operations)
            {
                var args = Arguments(operation);
                w.Line();
                w.Line($"public FakeCallAssertion {operation.Name}({string.Join(", ", args.Select(a => $"{Optional(a.Type)} {a.Identifier} = null"))})");
                w.Line($"    => new(_log, new {calls}().{operation.Name}({string.Join(", ", args.Select(a => a.Identifier))}));");
                if (args.Count > 0)
                {
                    w.Line();
                    w.Line($"public FakeCallAssertion {operation.Name}(Func<{string.Join(", ", args.Select(a => a.Type).Append("bool"))}> where)");
                    w.Line($"    => new(_log, new {calls}().{operation.Name}(where));");
                }
            }

            w.Close();
            w.Line();

            w.Line("/// <summary>Assertions on what the fake received, in the style of AwesomeAssertions. Each throws FakeAssertionException.</summary>");
            w.Open($"public sealed class {assertions}");
            w.Line($"private readonly {fake} _fake;");
            w.Line("private readonly FakeCallLog _log;");
            w.Line();
            w.Line($"internal {assertions}({fake} fake, FakeCallLog log) => (_fake, _log) = (fake, log);");
            w.Line();
            w.Line("/// <summary>A call, then how many times: <c>HaveReceived.GetPet(petId: 7).Twice()</c>.</summary>");
            w.Line($"public {received} HaveReceived => new(_log);");
            w.Line();
            w.Line("/// <summary>Calls matching each of these, in this order; other calls may come between.</summary>");
            w.Line($"public void HaveReceivedInOrder(params Func<{calls}, FakeCallMatcher>[] calls)");
            w.Line($"    => _log.HaveReceivedInOrder(calls.Select(c => c(new {calls}())).ToList());");
            w.Line();
            w.Line("/// <summary>Every call has been asserted on by an earlier HaveReceived or HaveReceivedInOrder.</summary>");
            w.Line("public void HaveReceivedNoOtherCalls() => _log.HaveReceivedNoOtherCalls();");
            w.Line();
            w.Line("/// <summary>The scenario given to UseScenario was followed: every step called as written, nothing unexpected.</summary>");
            w.Open("public void HaveMetItsScenario()");
            w.Line("var report = _fake.Verify();");
            w.Open("if (!report.Passed)");
            w.Line("throw new FakeAssertionException(\"Expected the fake to have met its scenario, but:\" + string.Concat(report.Failures.Select(f => \"\\n  - \" + f)));");
            w.Close();
            w.Close();
            w.Close();
        }

        private static List<Param> Arguments(Operation operation)
        {
            var args = operation.Parameters.ToList();
            if (operation.Body is not null)
            {
                args.Add(new Param("body", "body", operation.Body.Type, "FromBody"));
            }

            return args;
        }

        /// <summary>The type as an optional parameter: nullable, so null can mean "not given".</summary>
        private static string Optional(string type) => type.EndsWith("?", StringComparison.Ordinal) ? type : type + "?";

        private static string Field(Operation operation) => "_" + Naming.Camel(operation.Id).TrimStart('@');

        /// <summary>A real fake server for a test: Kestrel on a free port, the generated endpoints, a fluent fake behind them.</summary>
        private void EmitFakeServer(CodeWriter w, string fake, string hub)
        {
            var server = _apiName + "FakeServer";
            w.Line("/// <summary>");
            w.Line($"/// A real <c>{_apiName}</c> server for tests, on a free port: the generated endpoints (validation, problem+json, the");
            w.Line("/// OpenAPI document) over a fake set up fluently. <c>await using var server = await StartAsync();</c>");
            w.Line("/// </summary>");
            w.Open($"public sealed class {server} : IAsyncDisposable");
            w.Line("private readonly WebApplication _app;");
            w.Line();
            w.Line($"private {server}(WebApplication app, {fake} fake, Uri baseAddress) => (_app, Fake, BaseAddress) = (app, fake, baseAddress);");
            w.Line();
            w.Line("/// <summary>The fake answering at the server's root.</summary>");
            w.Line($"public {fake} Fake {{ get; }}");
            w.Line();
            w.Line("/// <summary>Where the server listens, ending in a slash.</summary>");
            w.Line("public Uri BaseAddress { get; }");
            w.Line();
            w.Line($"public {_apiName}FakeGiven Given => Fake.Given;");
            w.Line();
            w.Line($"public {_apiName}FakeAssertions Should() => Fake.Should();");
            w.Line();
            w.Line("public HttpClient CreateHttpClient() => new() { BaseAddress = BaseAddress };");
            w.Line();
            w.Line($"/// <summary>A typed client for the server, which checks every answer against the spec.</summary>");
            w.Line($"public {_apiName}Client CreateClient() => new(CreateHttpClient());");
            w.Line();
            w.Line("/// <summary>Starts the server on <paramref name=\"port\"/>, or a free port when 0.</summary>");
            w.Open($"public static async Task<{server}> StartAsync(int port = 0, CancellationToken cancellationToken = default)");
            w.Line("var builder = WebApplication.CreateSlimBuilder();");
            w.Line("builder.WebHost.UseUrls($\"http://127.0.0.1:{port}\");");
            w.Line("builder.Logging.ClearProviders();");
            w.Line($"builder.Services.Add{_apiName}();");
            w.Line("builder.Services.AddProblemDetails();");
            w.Line($"builder.Services.Add{_apiName}FakeHub();");
            if (_describesItself)
            {
                w.Line($"builder.Services.AddOpenApi(options => options.Add{_apiName}Schemas());");
            }

            w.Line();
            w.Line("var app = builder.Build();");
            w.Line($"app.Use{_apiName}FakeHub();");
            if (_describesItself)
            {
                w.Line("app.MapOpenApi();");
            }

            w.Line($"app.Map{_apiName}();");
            w.Line($"app.Map{_apiName}FakeControl();");
            w.Line("await app.StartAsync(cancellationToken).ConfigureAwait(false);");
            w.Line();
            w.Line("var address = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()");
            w.Line("    .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!.Addresses.First();");
            w.Line($"return new(app, app.Services.GetRequiredService<{hub}>().Default, new Uri(address.TrimEnd('/') + \"/\"));");
            w.Close();
            w.Line();
            w.Open("public async ValueTask DisposeAsync()");
            w.Line("await _app.StopAsync().ConfigureAwait(false);");
            w.Line("await _app.DisposeAsync().ConfigureAwait(false);");
            w.Close();
            w.Close();
            w.Line();
        }

        private static string Responder(Operation operation)
        {
            var types = operation.Parameters.Select(p => p.Type).ToList();
            if (operation.Body is not null)
            {
                types.Add(operation.Body.Type);
            }

            types.Add("CancellationToken");
            types.Add(operation.HandlerReturnType);
            return $"Func<{string.Join(", ", types)}>";
        }

        /// <summary>
        /// Hosting for fake servers: a hub of isolated fakes. The default fake answers at the root; each session, started
        /// with a scenario over HTTP, answers at <c>/sessions/{id}/</c>. Control endpoints are left out of the OpenAPI document.
        /// </summary>
        private string EmitFakeHosting(List<Operation> operations)
        {
            var w = new CodeWriter(_namespace);
            w.Usings("System", "System.Collections.Concurrent", "System.Linq", "System.Net.Http", "System.Text.Json", "System.Threading",
                "System.Threading.Tasks", "Microsoft.AspNetCore.Builder", "Microsoft.AspNetCore.Hosting", "Microsoft.AspNetCore.Http",
                "Microsoft.AspNetCore.Routing", "Microsoft.Extensions.DependencyInjection", "Microsoft.Extensions.Logging", "Matchmark.Fakes");
            var fake = _apiName + "Fake";
            var hub = _apiName + "FakeHub";

            w.Line("/// <summary>A scripted answer: a documented status and, optionally, its body.</summary>");
            w.Line("public sealed record FakeResponse(int Status, JsonElement? Body);");
            w.Line();
            w.Line($"/// <summary>The fakes a fake server hosts: one default, and one per session.</summary>");
            w.Open($"public sealed class {hub}");
            w.Line($"private readonly ConcurrentDictionary<string, {fake}> _sessions = new();");
            w.Line();
            w.Line($"public {fake} Default {{ get; }} = new();");
            w.Line();
            w.Open($"public (string Id, {fake} Fake) Start(Scenario? scenario)");
            w.Line($"var fake = new {fake}();");
            w.Open("if (scenario is not null)");
            w.Line("fake.UseScenario(scenario);");
            w.Close();
            w.Line();
            w.Line("var id = Guid.NewGuid().ToString(\"N\")[..12];");
            w.Line("_sessions[id] = fake;");
            w.Line("return (id, fake);");
            w.Close();
            w.Line();
            w.Line($"public {fake}? Find(string id) => _sessions.TryGetValue(id, out var fake) ? fake : null;");
            w.Line();
            w.Line("public bool End(string id) => _sessions.TryRemove(id, out _);");
            w.Close();
            w.Line();

            var interfaces = operations.Select(o => HandlerInterface(o.Tag)).Distinct().ToList();
            EmitFakeServer(w, fake, hub);
            w.Open($"public static class {_apiName}FakeHosting");
            w.Line("private const string SessionKey = \"Matchmark.Fakes.Session\";");
            w.Line();
            w.Line("/// <summary>Registers the hub and answers each request from its session's fake, or the default one.</summary>");
            w.Open($"public static IServiceCollection Add{_apiName}FakeHub(this IServiceCollection services)");
            w.Line($"var hub = new {hub}();");
            w.Line("services.AddSingleton(hub);");
            w.Line("services.AddSingleton(hub.Default);");
            w.Line("services.AddHttpContextAccessor();");
            foreach (var iface in interfaces)
            {
                w.Line($"services.AddScoped<{iface}>(sp => sp.GetRequiredService<IHttpContextAccessor>().HttpContext?.Items[SessionKey] as {fake} ?? hub.Default);");
            }

            w.Line("return services;");
            w.Close();
            w.Line();
            w.Line("/// <summary>");
            w.Line("/// Serves each session's fake under <c>{sessions}/{id}/</c>, and turns a call its scenario didn't expect into a");
            w.Line("/// 500 problem titled \"Fake expectation failed\". Adds routing after itself, so call it before mapping endpoints.");
            w.Line("/// </summary>");
            w.Open($"public static IApplicationBuilder Use{_apiName}FakeHub(this IApplicationBuilder app, string sessions = \"/sessions\")");
            w.Line($"var hub = app.ApplicationServices.GetRequiredService<{hub}>();");
            w.Line("app.Use(async (context, next) =>");
            w.Line("{");
            w.Line("    if (context.Request.Path.StartsWithSegments(sessions, out var rest) && rest.HasValue)");
            w.Line("    {");
            w.Line("        var parts = rest.Value!.Split('/', 3);");
            w.Line("        if (hub.Find(parts[1]) is not { } fake)");
            w.Line("        {");
            w.Line("            await Results.Problem($\"There is no fake session '{parts[1]}'.\", statusCode: 404).ExecuteAsync(context);");
            w.Line("            return;");
            w.Line("        }");
            w.Line();
            w.Line("        context.Items[SessionKey] = fake;");
            w.Line("        context.Request.PathBase = context.Request.PathBase.Add($\"{sessions}/{parts[1]}\");");
            w.Line("        context.Request.Path = \"/\" + (parts.Length > 2 ? parts[2] : string.Empty);");
            w.Line("    }");
            w.Line();
            w.Line("    try");
            w.Line("    {");
            w.Line("        await next(context);");
            w.Line("    }");
            w.Line("    catch (FakeExpectationException e) when (!context.Response.HasStarted)");
            w.Line("    {");
            w.Line("        await Results.Problem(e.Message, title: \"Fake expectation failed\", statusCode: 500).ExecuteAsync(context);");
            w.Line("    }");
            w.Line("});");
            w.Line("app.UseRouting();");
            w.Line("return app;");
            w.Close();
            w.Line();
            w.Line("/// <summary>");
            w.Line("/// The control API, under <c>{prefix}</c>, left out of the OpenAPI document:");
            w.Line("/// <c>PUT /{operationId}</c> scripts the default fake (<c>{\"status\": 404, \"body\": {...}}</c>), <c>GET /calls</c> lists");
            w.Line("/// its calls, <c>DELETE /</c> resets it; <c>POST /sessions</c> with a scenario (or nothing) stands up an isolated fake");
            w.Line("/// and returns its base URL, <c>GET /sessions/{id}</c> verifies it, <c>DELETE /sessions/{id}</c> ends it.");
            w.Line("/// </summary>");
            w.Open($"public static IEndpointRouteBuilder Map{_apiName}FakeControl(this IEndpointRouteBuilder app, string prefix = \"/__fake\", string sessions = \"/sessions\")");
            w.Line($"var hub = app.ServiceProvider.GetRequiredService<{hub}>();");
            w.Line("var group = app.MapGroup(prefix).ExcludeFromDescription();");
            w.Line("group.MapPut(\"/{operation}\", (string operation, FakeResponse response) =>");
            w.Line("{");
            w.Line("    try");
            w.Line("    {");
            w.Line("        return hub.Default.Respond(operation, response.Status, response.Body?.GetRawText())");
            w.Line("            ? Results.NoContent()");
            w.Line("            : Results.Problem($\"The spec documents no {response.Status} response for '{operation}'.\", statusCode: 404);");
            w.Line("    }");
            w.Line("    catch (ArgumentException e)");
            w.Line("    {");
            w.Line("        return Results.Problem(e.Message, statusCode: 400);");
            w.Line("    }");
            w.Line("});");
            w.Line("group.MapGet(\"/calls\", () => Results.Ok(hub.Default.Calls));");
            w.Line("group.MapDelete(\"/\", () =>");
            w.Line("{");
            w.Line("    hub.Default.Reset();");
            w.Line("    return Results.NoContent();");
            w.Line("});");
            w.Line("group.MapPost(\"/sessions\", async (HttpRequest request) =>");
            w.Line("{");
            w.Line("    var text = await new System.IO.StreamReader(request.Body).ReadToEndAsync();");
            w.Line("    Scenario? scenario;");
            w.Line("    try");
            w.Line("    {");
            w.Line("        scenario = string.IsNullOrWhiteSpace(text) ? null : Scenario.FromJson(text);");
            w.Line("    }");
            w.Line("    catch (JsonException e)");
            w.Line("    {");
            w.Line("        return Results.Problem(\"The scenario is not valid JSON: \" + e.Message, statusCode: 400);");
            w.Line("    }");
            w.Line();
            w.Line($"    var problems = scenario is null ? [] : {fake}.Check(scenario);");
            w.Line("    if (problems.Count > 0)");
            w.Line("    {");
            w.Line("        return Results.Problem(string.Join(\"\\n\", problems), title: \"The scenario breaks the spec\", statusCode: 400);");
            w.Line("    }");
            w.Line();
            w.Line("    var (id, _) = hub.Start(scenario);");
            w.Line("    return Results.Created($\"{prefix}/sessions/{id}\", new { id, baseUrl = $\"{sessions}/{id}/\" });");
            w.Line("});");
            w.Line("group.MapGet(\"/sessions/{id}\", (string id) => hub.Find(id) is { } fake ? Results.Ok(fake.Verify()) : Results.NotFound());");
            w.Line("group.MapDelete(\"/sessions/{id}\", (string id) => hub.End(id) ? Results.NoContent() : Results.NotFound());");
            w.Line("return app;");
            w.Close();
            w.Close();
            return w.ToString();
        }

        // ---------------------------------------------------------------- endpoints

        private string EmitEndpoints(List<Operation> operations)
        {
            var w = new CodeWriter(_namespace);
            w.Usings(
                "System",
                "System.Threading",
                "System.Threading.Tasks",
                "Microsoft.AspNetCore.Builder",
                "Microsoft.AspNetCore.Http",
                "Microsoft.AspNetCore.Http.HttpResults",
                "Microsoft.AspNetCore.Mvc",
                "Microsoft.AspNetCore.Routing",
                "System.Collections.Generic",
                "System.Text.Json",
                "System.Text.RegularExpressions");

            w.Open($"public static class {_apiName}Endpoints");
            w.Line("/// <summary>Configures JSON the way the spec reads: numbers are numbers, and a oneOf discriminator may come anywhere.</summary>");
            w.Open($"public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection Add{_apiName}(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
            w.Line($"// A request that can't be bound throws, so Use{_apiName}Problems can answer it as the spec's validation problem.");
            w.Line("global::Microsoft.Extensions.DependencyInjection.OptionsServiceCollectionExtensions.Configure<RouteHandlerOptions>(services, options => options.ThrowOnBadRequest = true);");
            w.Open("return global::Microsoft.Extensions.DependencyInjection.HttpJsonServiceExtensions.ConfigureHttpJsonOptions(services, options =>", braces: false);
            w.Line("{");
            w.Line("    options.SerializerOptions.NumberHandling = global::System.Text.Json.Serialization.JsonNumberHandling.Strict;");
            w.Line("    options.SerializerOptions.AllowOutOfOrderMetadataProperties = true;");
            w.Line($"    options.SerializerOptions.TypeInfoResolver = global::System.Text.Json.Serialization.Metadata.JsonTypeInfoResolver.WithAddedModifier(options.SerializerOptions.TypeInfoResolver ?? new global::System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(), {ValidationClass}.RefuseNulls);");
            w.Line("});");
            w.Close();
            w.Line();
            w.Line("/// <summary>");
            w.Line("/// Maps every operation in the spec onto its handler interface, which must be registered in DI. On an application");
            w.Line($"/// (not a route group) it also adds Use{_apiName}Problems, so requests that can't be bound get the spec's validation problem.");
            w.Line("/// </summary>");
            w.Open($"public static IEndpointRouteBuilder Map{_apiName}(this IEndpointRouteBuilder app)");
            w.Line("if (app is IApplicationBuilder pipeline)");
            w.Line("{");
            w.Line($"    pipeline.Use{_apiName}Problems();");
            w.Line("}");
            w.Line();

            foreach (var operation in operations)
            {
                EmitEndpoint(w, operation);
            }

            w.Line("return app;");
            w.Close();
            w.Line();
            w.Block(ProblemsMiddleware.Replace("{Api}", _apiName).Replace("{Validation}", ValidationClass));
            w.Line();
            w.Line("/// <summary>Writes a problem as application/problem+json with the status the spec gives it, whatever the handler set.</summary>");
            w.Open("private static ProblemHttpResult Problem(ProblemDetails problem, int status)");
            w.Line("problem.Status = status;");
            w.Line("return TypedResults.Problem(problem);");
            w.Close();
            w.Close();
            return w.ToString();
        }

        /// <summary>
        /// The answer to a request the endpoints can't bind. ASP.NET Core would write an empty 400 (or, for a body missing its
        /// oneOf discriminator, a 500); the spec documents a validation problem naming what was wrong. Found by the
        /// regression suite generated from the spec.
        /// </summary>
        private const string ProblemsMiddleware = @"/// <summary>
/// Answers a request that can't be bound (a body that isn't the schema's JSON, a parameter of the wrong type) with the
/// spec's validation problem, naming the value that was wrong, instead of an empty 400 or a 500.
/// </summary>
public static IApplicationBuilder Use{Api}Problems(this IApplicationBuilder app)
    => app.Use(async (context, next) =>
    {
        try
        {
            await next(context).ConfigureAwait(false);
        }
        catch (BadHttpRequestException e) when (!context.Response.HasStarted && e.StatusCode != StatusCodes.Status400BadRequest)
        {
            await TypedResults.Problem(statusCode: e.StatusCode).ExecuteAsync(context).ConfigureAwait(false);
        }
        catch (Exception e) when (!context.Response.HasStarted && BindingError(e) is { } error)
        {
            await TypedResults.ValidationProblem(new Dictionary<string, string[]> { [error.Key] = [error.Message] }).ExecuteAsync(context).ConfigureAwait(false);
        }
    });

/// <summary>The value a binding failure is about, and what was wrong with it; null for any other exception.</summary>
private static (string Key, string Message)? BindingError(Exception exception) => exception switch
{
    BadHttpRequestException { InnerException: JsonException json } => {Validation}.BindingError(json.Path ?? ""$"", json.Message),
    BadHttpRequestException bad => {Validation}.BindingError(
        Regex.Match(bad.Message, @""parameter \x22(?:[^\x22]* )?([^\x22 ]+)\x22"") is { Success: true } parameter ? parameter.Groups[1].Value : ""body"", bad.Message),
    NotSupportedException { Source: ""System.Text.Json"" } unsupported when unsupported.Message.Contains(""type discriminator"", StringComparison.Ordinal)
        => {Validation}.BindingError(Regex.Match(unsupported.Message, @""Path: (\$\S*)"").Groups[1].Value, unsupported.Message),
    _ => null,
};";

        private void EmitEndpoint(CodeWriter w, Operation operation)
        {
            var args = operation.Parameters.Where(p => !p.Deep)
                .Select(p => p.Enum ? $"[{p.Attribute}(Name = {Naming.Literal(p.Name)})] string? __text_{p.Identifier.TrimStart('@')}" : $"[{p.Attribute}(Name = {Naming.Literal(p.Name)})] {p.Type} {p.Identifier}")
                .ToList();
            if (operation.Body is { Form: true } or { Text: true } || operation.Parameters.Any(p => p.Deep))
            {
                args.Add("HttpRequest __http");
            }

            if (operation.Body is { Form: true } or { Text: true })
            {
            }
            else if (operation.Body is not null)
            {
                args.Add($"[FromBody] {operation.Body.Type} body");
            }

            args.Add($"[FromServices] {HandlerInterface(operation.Tag)} __handlers");
            if (operation.ResponseHeaders.Count > 0)
            {
                args.Add("HttpContext __context");
            }

            args.Add("CancellationToken __cancellationToken");

            var call = new List<string>(operation.Parameters.Select(p => p.Identifier));
            if (operation.Body is not null)
            {
                call.Add("body");
            }

            call.Add("__cancellationToken");

            var returnType = operation.EndpointReturnType;
            w.Line($"app.MapMethods({Naming.Literal(operation.Path)}, new[] {{ {Naming.Literal(operation.Method)} }}, static async Task<{returnType}> ({string.Join(", ", args)}) =>");
            w.Open(string.Empty);
            foreach (var deep in operation.Parameters.Where(p => p.Deep))
            {
                EmitDeepRead(w, deep, "__http.Query", "__cancellationToken", "throw new BadHttpRequestException(\"The query doesn't match the schema.\", StatusCodes.Status400BadRequest, {0});");
            }

            EmitEnumReads(w, operation, "return TypedResults.ValidationProblem({0});");

            if (operation.Body is { Text: true } text)
            {
                // Text, read as it is: [FromBody] string would want JSON.
                w.Open($"if (__http.ContentType?.StartsWith({Naming.Literal(text.MediaType)}, StringComparison.OrdinalIgnoreCase) != true)");
                w.Line($"throw new BadHttpRequestException(\"The body must be {text.MediaType}.\", StatusCodes.Status415UnsupportedMediaType);");
                w.Close();
                w.Line();
                w.Line("var body = await new global::System.IO.StreamReader(__http.Body).ReadToEndAsync(__cancellationToken).ConfigureAwait(false);");
                w.Line($"if ({ValidationClass}.CheckText(body, {(text.Schema.MinLength ?? (operation.RequiredBody ? 1 : 0)).Invariant()}, {(text.Schema.MaxLength?.Invariant() ?? "null")}, {(text.Schema.Pattern is { } textPattern ? Naming.Literal(textPattern) : "null")}) is {{ }} __textErrors)");
                w.Line("{");
                w.Line("    return TypedResults.ValidationProblem(__textErrors);");
                w.Line("}");
                w.Line();
            }

            if (operation.Body is { Form: true } form)
            {
                // Read here rather than bound with [FromForm], so that nested values in brackets work and a form that
                // doesn't fit the schema is the same validation problem a JSON body would be.
                w.Open("if (!__http.HasFormContentType)");
                w.Line($"throw new BadHttpRequestException(\"The body must be {FormMedia}.\", StatusCodes.Status415UnsupportedMediaType);");
                w.Close();
                w.Line();
                w.Line($"{form.Type} body;");
                w.Open("try");
                w.Line($"body = {ValidationClass}.ReadForm<{form.Type}>(global::System.Linq.Enumerable.SelectMany(await __http.ReadFormAsync(__cancellationToken).ConfigureAwait(false), f => global::System.Linq.Enumerable.Select(f.Value, v => new KeyValuePair<string, string>(f.Key, v ?? string.Empty))));");
                w.Close();
                w.Open("catch (JsonException e)");
                w.Line("throw new BadHttpRequestException(\"The form doesn't match the schema.\", StatusCodes.Status400BadRequest, e);");
                w.Close();
                w.Line();
            }

            var bounds = ParameterChecks(operation).ToList();
            if (bounds.Count > 0)
            {
                // Bound parameters, checked against the spec's constraints: the binding only checks their type.
                w.Line("var __invalid = new Dictionary<string, string[]>();");
                foreach (var check in bounds)
                {
                    w.Line(check);
                }

                w.Line("if (__invalid.Count > 0)");
                w.Line("{");
                w.Line("    return TypedResults.ValidationProblem(__invalid);");
                w.Line("}");
                w.Line();
            }

            if (operation.Dependencies.Count > 0)
            {
                var given = operation.Parameters.Select(p => $"[{Naming.Literal(p.Name)}] = {ValidationClass}.Text({p.Identifier})");
                w.Line($"if ({ValidationClass}.Broken{operation.Name}Dependencies(new Dictionary<string, string?> {{ {string.Join(", ", given)} }}) is {{ }} __broken)");
                w.Line("{");
                w.Line("    return TypedResults.ValidationProblem(__broken);");
                w.Line("}");
                w.Line();
            }

            if (operation.Body is { Validated: true })
            {
                w.Line($"if ({ValidationClass}.Validate(body) is {{ }} __errors)");
                w.Line("{");
                w.Line("    return TypedResults.ValidationProblem(__errors);");
                w.Line("}");
                w.Line();
            }

            var invoke = $"await __handlers.{operation.Name}({string.Join(", ", call)}).ConfigureAwait(false)";
            var hasValue = operation.SuccessIsUnion || operation.Successes[0].BodyType is not null;
            var headers = operation.ResponseHeaders.Count > 0;
            if (headers)
            {
                w.Line($"using var __headers = {_apiName}Headers.Begin();");
            }

            // The response headers the handler set, written onto the response before it's returned.
            var apply = headers ? "foreach (var __header in __headers.Values) __context.Response.Headers[__header.Key] = __header.Value;" : null;
            if (operation.Errors.Count == 0)
            {
                if (!hasValue)
                {
                    w.Line($"{invoke};");
                    if (apply is not null)
                    {
                        w.Line(apply);
                    }

                    w.Line($"return {operation.Successes[0].Factory("_")};");
                }
                else
                {
                    w.Line($"var __value = {invoke};");
                    if (apply is not null)
                    {
                        w.Line(apply);
                    }

                    w.Line($"return {Success(operation, "__value", returnType)};");
                }
            }
            else
            {
                w.Line($"var __result = {invoke};");
                if (apply is not null)
                {
                    w.Line(apply);
                }

                w.Line($"return __result.Match<{returnType}>(");
                w.Line($"    __success => {Success(operation, "__success", returnType)},");
                w.Line($"    __error => __error.Match<{returnType}>({string.Join(", ", operation.Errors.Select(e => $"__case => {e.Factory("__case.Body")}"))}));");
            }

            w.Close(braces: true, suffix: ")");
            w.Line($"    .WithName({Naming.Literal(operation.Id)})");
            foreach (var problem in operation.Errors.Where(e => e.IsProblem && !e.IsValidation))
            {
                // ProblemHttpResult carries no response metadata of its own; document each status from the spec.
                w.Line($"    .Produces<{problem.BodyType}>({problem.Status.Invariant()}, \"application/problem+json\")");
            }

            if (operation.Untyped && (operation.Body is not null || operation.Dependencies.Count > 0 || operation.ValidatesParameters))
            {
                w.Line("    .ProducesValidationProblem()");
            }

            foreach (var response in operation.Successes.Concat(operation.Errors).Where(r => r.NeedsProduces || operation.Untyped && !r.IsProblem && !r.IsValidation))
            {
                // Written by number, so documented here: TypedResults.Json and StatusCode declare nothing themselves.
                w.Line(response.BodyType is null || response.IsRedirect
                    ? $"    .Produces({response.Status.Invariant()})"
                    : $"    .Produces<{response.BodyType}>({response.Status.Invariant()}, {Naming.Literal(response.MediaType ?? "application/json")})");
            }

            w.Line($"    .WithTags({Naming.Literal(operation.Tag)});");
            w.Line();
        }

        private static string Success(Operation operation, string value, string returnType)
            => operation.SuccessIsUnion
                ? $"{value}.Match<{returnType}>({string.Join(", ", operation.Successes.Select(s => $"__case => {s.Factory("__case.Body")}"))})"
                : operation.Successes[0].Factory(value);

        private void Unsupported(string what, string why)
            => Diagnostics.Add(Diagnostic.Create(Generator.Diagnostics.Unsupported, _location, what, why));
    }

    internal sealed class CodeWriter
    {
        private readonly StringBuilder _builder = new();
        private readonly string _namespace;
        private int _indent;

        public CodeWriter(string ns)
        {
            _namespace = ns;
            _builder.Append("// <auto-generated/>\n#nullable enable\n\n");
        }

        public void Usings(params string[] namespaces)
        {
            foreach (var ns in namespaces)
            {
                _builder.Append("using ").Append(ns).Append(";\n");
            }

            _builder.Append("\nnamespace ").Append(_namespace).Append(";\n\n");
        }

        public void Line(string text = "")
        {
            if (text.Length > 0)
            {
                _builder.Append(' ', _indent * 4).Append(text);
            }

            _builder.Append('\n');
        }

        public void Open(string header, bool braces = true)
        {
            if (header.Length > 0)
            {
                Line(header);
            }

            if (braces)
            {
                Line("{");
                _indent++;
            }
        }

        public void Close(bool braces = true, string suffix = "")
        {
            if (braces)
            {
                _indent--;
                Line("}" + suffix);
            }
        }

        /// <summary>Several lines at the current indentation.</summary>
        public void Block(string text)
        {
            foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
            {
                Line(line);
            }
        }

        public void TrimBlankLine()
        {
            if (_builder.Length >= 2 && _builder[_builder.Length - 1] == '\n' && _builder[_builder.Length - 2] == '\n')
            {
                _builder.Length--;
            }
        }

        public override string ToString() => _builder.ToString();
    }
}
