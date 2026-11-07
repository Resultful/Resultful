using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.OpenApi;

namespace Symbolon.Generator
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

        private readonly OpenApiDocument _document;
        private readonly string _namespace;
        private readonly string _apiName;
        private readonly Location _location;

        /// <summary>Generated component schemas by name.</summary>
        private readonly Dictionary<string, IOpenApiSchema> _schemas = new(StringComparer.Ordinal);

        /// <summary>Union member schema → (base type, discriminator property, discriminator value).</summary>
        private readonly Dictionary<string, (string Base, string Property, string Value)> _unionMembers = new(StringComparer.Ordinal);

        private readonly bool _describesItself;
        private readonly bool _hasFakes;

        public Emitter(OpenApiDocument document, string ns, string apiName, Location location, bool describesItself = false, bool hasFakes = false)
        {
            _describesItself = describesItself;
            _hasFakes = hasFakes;
            _document = document;
            _namespace = ns;
            _apiName = apiName;
            _location = location;
        }

        public List<Diagnostic> Diagnostics { get; } = new();

        private string ValidationClass => _apiName + "Validation";

        public Dictionary<string, string> Emit(bool contract = true, bool server = true, bool fakeServer = false, bool regression = false)
        {
            CollectSchemas();
            var operations = CollectOperations();
            var files = new Dictionary<string, string>();

            if (contract)
            {
                files["Models"] = EmitModels();
                files["Validation"] = EmitValidation(operations);
                files["Responses"] = EmitResponses(operations);
                files["Handlers"] = EmitHandlers(operations);
                files["Client"] = EmitClient(operations);
                if (_hasFakes)
                {
                    files["Fake"] = EmitFake(operations);
                }
            }

            if (server)
            {
                files["Endpoints"] = EmitEndpoints(operations);
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
                files["Regression"] = EmitRegression();
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
            if (components is null)
            {
                return;
            }

            foreach (var pair in components)
            {
                if (WellKnownSchemas.ContainsKey(pair.Key))
                {
                    continue;
                }

                var schema = pair.Value;
                if (schema.OneOf is { Count: > 0 })
                {
                    if (!TryCollectUnion(pair.Key, schema))
                    {
                        continue;
                    }
                }
                else if (!IsEnum(schema) && !IsObject(schema))
                {
                    Unsupported($"Schema '{pair.Key}'", "only objects, string enums and discriminated oneOf are generated");
                    continue;
                }

                _schemas[pair.Key] = schema;
            }
        }

        private bool TryCollectUnion(string name, IOpenApiSchema schema)
        {
            var property = schema.Discriminator?.PropertyName;
            if (string.IsNullOrEmpty(property))
            {
                Unsupported($"Schema '{name}'", "oneOf without a discriminator");
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

        private static bool IsObject(IOpenApiSchema schema)
            => (schema.Type is null || Has(schema, JsonSchemaType.Object)) && schema.Properties is { Count: > 0 };

        private static bool Has(IOpenApiSchema schema, JsonSchemaType type)
            => schema.Type is { } t && (t & type) == type;

        private static string? RefId(IOpenApiSchema schema)
            => schema is OpenApiSchemaReference reference ? reference.Reference.Id : null;

        /// <summary>The C# type for a schema used as a property, parameter or body.</summary>
        private string? TypeOf(IOpenApiSchema schema, string context)
        {
            if (RefId(schema) is { } id)
            {
                if (WellKnownSchemas.TryGetValue(id, out var wellKnown))
                {
                    return wellKnown;
                }

                if (_schemas.ContainsKey(id))
                {
                    return Naming.Pascal(id);
                }

                Unsupported(context, $"$ref to skipped or missing schema '{id}'");
                return null;
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
                    return TypeOf(schema.Items, context) is { } item
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
            => type is "int" or "long" or "float" or "double" or "decimal" or "bool"
               || type.StartsWith("global::System.DateTimeOffset", StringComparison.Ordinal)
               || type.StartsWith("global::System.DateOnly", StringComparison.Ordinal)
               || type.StartsWith("global::System.Guid", StringComparison.Ordinal);

        private bool IsEnumRef(IOpenApiSchema schema)
            => RefId(schema) is { } id && _schemas.TryGetValue(id, out var target) && IsEnum(target);

        private string EmitModels()
        {
            var w = new CodeWriter(_namespace);
            w.Usings("System.Text.Json.Serialization");

            foreach (var pair in _schemas)
            {
                var name = Naming.Pascal(pair.Key);
                var schema = pair.Value;

                if (IsEnum(schema))
                {
                    w.Line($"[JsonConverter(typeof(JsonStringEnumConverter<{name}>))]");
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

                if (schema.OneOf is { Count: > 0 })
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
                w.Open(hasBase ? $"public sealed record {name} : {Naming.Pascal(union.Base)}" : $"public sealed record {name}");
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
                    foreach (var annotation in Annotations(property.Value))
                    {
                        w.Line(annotation);
                    }

                    w.Line($"public {(required ? "required " : string.Empty)}{type}{(nullable ? "?" : string.Empty)} {Naming.Pascal(property.Key)} {{ get; init; }}");
                    w.Line();
                }

                w.TrimBlankLine();
                w.Close();
                w.Line();
            }

            return w.ToString();
        }

        /// <summary>
        /// DataAnnotations mirroring the constraints. They are not what validates (the generated validator is); they
        /// let ASP.NET Core's own OpenAPI document carry the constraints, so the round-trip check can compare them.
        /// </summary>
        private static IEnumerable<string> Annotations(IOpenApiSchema schema)
        {
            if (schema.MinLength is { } min)
            {
                yield return $"[global::System.ComponentModel.DataAnnotations.MinLength({min.Invariant()})]";
            }

            if (schema.MaxLength is { } max)
            {
                yield return $"[global::System.ComponentModel.DataAnnotations.MaxLength({max.Invariant()})]";
            }

            if (schema.Pattern is { } pattern)
            {
                yield return $"[global::System.ComponentModel.DataAnnotations.RegularExpression({Naming.Literal(pattern)})]";
            }

            if (schema.Minimum is { } minimum && schema.Maximum is { } maximum)
            {
                yield return $"[global::System.ComponentModel.DataAnnotations.Range({Number(minimum)}, {Number(maximum)})]";
            }
        }

        // ---------------------------------------------------------------- validation

        /// <summary>
        /// The translation of a binding failure (ASP.NET Core's key and message) into the spec's terms, shared by the
        /// generated endpoints and the scaffolded controllers. Found necessary by the regression suite generated from the spec.
        /// </summary>
        private const string BindingErrorMethod = @"/// <summary>
/// A request that couldn't be bound, in the spec's terms: which value was wrong (<c>name</c>, <c>details.goodBoy</c>,
/// <c>petId</c>, or <c>body</c> for the whole body) and what was wrong with it. <paramref name=""key""/> is where ASP.NET Core
/// put the failure (a JSON path such as <c>$.name</c>, or a parameter name) and <paramref name=""message""/> its message.
/// The generated endpoints and the scaffolded controllers both answer through this, so they answer alike.
/// </summary>
public static (string Key, string Message) BindingError(string? key, string message)
{
    var at = key is null || key.Length == 0 || key == ""$"" ? string.Empty : key.StartsWith(""$."", System.StringComparison.Ordinal) ? key.Substring(2) : key.TrimStart('$');
    var missing = Regex.Match(message, ""missing required properties including: '([^']+)'"");
    if (missing.Success)
    {
        return (at.Length == 0 ? missing.Groups[1].Value : at + ""."" + missing.Groups[1].Value, $""'{missing.Groups[1].Value}' is required."");
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
            w.Usings("System.Collections.Generic", "System.Text.RegularExpressions");

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

                if (schema.OneOf is { Count: > 0 })
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

                    var member = "value." + Naming.Pascal(property.Key);
                    var jsonPath = $"path + {Naming.Literal(property.Key)}";
                    var required = schema.Required?.Contains(property.Key) == true;
                    var prop = property.Value;
                    var local = "v" + Naming.Pascal(property.Key);

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

                    if (prop.Minimum is { } minimum)
                    {
                        checks.Add($"if ({local} < {Number(minimum)}) errors.Add({jsonPath}, \"Must be at least {Number(minimum)}.\");");
                    }

                    if (prop.Maximum is { } maximum)
                    {
                        checks.Add($"if ({local} > {Number(maximum)}) errors.Add({jsonPath}, \"Must be at most {Number(maximum)}.\");");
                    }

                    if (RefId(prop) is { } refId && _schemas.ContainsKey(refId) && !IsEnumRef(prop))
                    {
                        checks.Add($"Check({local}, {jsonPath} + \".\", errors);");
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

                w.Close();
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

            /// <summary>The body's schema, for turning a sample into C#.</summary>
            public IOpenApiSchema? Schema { get; set; }

            public string CaseName => Naming.StatusNames[Status];

            /// <summary>
            /// A ProblemDetails body. TypedResults.NotFound(problem) and friends write it as application/json, so it
            /// goes out through TypedResults.Problem, which writes application/problem+json.
            /// </summary>
            public bool IsProblem => BodyType is { } type && WellKnownSchemas.ContainsValue(type);

            /// <summary>The <c>Microsoft.AspNetCore.Http.HttpResults</c> type this response is written as.</summary>
            public string ResultType => Status == 401
                ? "UnauthorizedHttpResult"
                : IsValidation ? "ValidationProblem"
                : IsProblem ? "ProblemHttpResult"
                : BodyType is null ? CaseName : $"{CaseName}<{BodyType}>";

            /// <summary>The TypedResults call that produces it, given the body expression.</summary>
            public string Factory(string body) => (Status, BodyType is null) switch
            {
                _ when IsValidation => $"TypedResults.ValidationProblem({body}.Errors, {body}.Detail, {body}.Instance, {body}.Title, {body}.Type)",
                _ when IsProblem => $"Problem({body}, {Status.Invariant()})",
                (201 or 202, false) => $"TypedResults.{CaseName}((string?)null, {body})",
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

            public Body? Body { get; set; }

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
                    if (Body is not null)
                    {
                        types.Add("ValidationProblem");
                    }

                    types.AddRange(Errors.Select(r => r.ResultType));

                    // Every problem response is one ProblemHttpResult; its status comes from the body.
                    return types.Distinct().ToList();
                }
            }

            public string EndpointReturnType => ResultTypes.Count == 1 ? ResultTypes[0] : $"Results<{string.Join(", ", ResultTypes)}>";
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

                    var tag = spec.Tags?.FirstOrDefault()?.Name ?? _apiName;
                    var operation = new Operation(spec.OperationId!, method, path.Key, tag);
                    if (TryBuild(operation, spec, path.Value.Parameters))
                    {
                        operations.Add(operation);
                    }
                }
            }

            return operations;
        }

        private bool TryBuild(Operation operation, OpenApiOperation spec, IList<IOpenApiParameter>? pathParameters)
        {
            var context = $"Operation '{operation.Id}'";
            var parameters = (pathParameters ?? Array.Empty<IOpenApiParameter>()).Concat(spec.Parameters ?? Array.Empty<IOpenApiParameter>());
            foreach (var parameter in parameters)
            {
                var attribute = parameter.In switch
                {
                    ParameterLocation.Path => "FromRoute",
                    ParameterLocation.Query => "FromQuery",
                    ParameterLocation.Header => "FromHeader",
                    _ => null,
                };

                if (attribute is null || parameter.Schema is null || TypeOf(parameter.Schema, $"{context} parameter '{parameter.Name}'") is not { } type)
                {
                    Unsupported($"{context} parameter '{parameter.Name}'", $"{parameter.In} parameters");
                    return false;
                }

                var nullable = !parameter.Required ? "?" : string.Empty;
                operation.Parameters.Add(new Param(parameter.Name!, Naming.Camel(parameter.Name!), type + nullable, attribute, parameter.Schema));
            }

            if (spec.RequestBody is { } requestBody)
            {
                if (JsonSchema(requestBody.Content) is not { } bodySchema || TypeOf(bodySchema, context + " request body") is not { } bodyType)
                {
                    Unsupported(context + " request body", "request bodies must be a JSON $ref");
                    return false;
                }

                operation.Body = new Body(bodyType, bodySchema);
            }

            foreach (var pair in spec.Responses ?? new OpenApiResponses())
            {
                if (!int.TryParse(pair.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var status) || !Naming.StatusNames.ContainsKey(status))
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

                    bodyType = TypeOf(responseSchema, $"{context} response '{pair.Key}'");
                    if (bodyType is null)
                    {
                        return false;
                    }

                    var target = responseSchema.Items is { } items && Has(responseSchema, JsonSchemaType.Array) ? items : responseSchema;
                    isList = !ReferenceEquals(target, responseSchema);
                    schemaName = RefId(target) is { } id && _schemas.TryGetValue(id, out var s) && !IsEnum(s) ? Naming.Pascal(id) : null;
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

            if (operation.Successes.Count == 0)
            {
                Unsupported(context, "operations without a 2xx response");
                return false;
            }

            if (operation.Body is not null && !operation.ValidationDocumented)
            {
                Diagnostics.Add(Diagnostic.Create(Generator.Diagnostics.UndocumentedValidation, _location, operation.Id));
            }

            if (operation.ResultTypes.Count > 6)
            {
                Unsupported(context, "more than 6 responses (Results<...> takes at most 6)");
                return false;
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
                w.Line(response.BodyType is null
                    ? $"public static {unionName} {response.CaseName}() => new {caseName}();"
                    : $"public static {unionName} {response.CaseName}({response.BodyType} body) => new {caseName}(body);");
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
                var validation = operation.Errors.FirstOrDefault(e => e.IsValidation);

                w.Line();
                if (validation is null)
                {
                    w.Line($"public {operation.HandlerReturnType} {operation.Name}({string.Join(", ", args)})");
                    w.Line($"    => {forward};");
                    continue;
                }

                var hasValue = operation.SuccessIsUnion || operation.Successes[0].BodyType is not null;
                var problem = $"new {validation.BodyType}(errors) {{ Title = \"One or more validation errors occurred.\", Status = 400 }}";
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
                foreach (var response in operation.Successes.Concat(operation.Errors))
                {
                    w.Line($"    ({Naming.Literal(operation.Id)}, {response.Status.Invariant()}),");
                }
            }

            w.Line("];");
            w.Close();
            w.Line();

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

            var interfaces = string.Join(", ", operations.Select(o => HandlerInterface(o.Tag)).Distinct());
            w.Line($"/// <summary>Calls <c>{_apiName}</c> over HTTP through the same interfaces its handlers implement. Relative to the HttpClient's BaseAddress.</summary>");
            w.Open($"public sealed partial class {_apiName}Client : {interfaces}");
            w.Line("private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)");
            w.Line("{");
            w.Line("    NumberHandling = JsonNumberHandling.Strict,");
            w.Line("    AllowOutOfOrderMetadataProperties = true,");
            w.Line("};");
            w.Line();
            w.Line("private readonly HttpClient _http;");
            w.Line("private readonly ConcurrentDictionary<(string Operation, int Status), byte> _observed = new();");
            w.Line();
            w.Line($"public {_apiName}Client(HttpClient http) => _http = http;");
            w.Line();
            w.Line("/// <summary>The documented outcomes this client has received, for measuring which ones a test suite reaches.</summary>");
            w.Line("public IReadOnlyCollection<(string Operation, int Status)> Observed => _observed.Keys.ToList();");

            foreach (var operation in operations)
            {
                w.Line();
                EmitClientMethod(w, operation);
            }

            w.Line();
            w.Line("private static string Text(object value) => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;");
            w.Line();
            w.Line("private static string Segment(object value) => Uri.EscapeDataString(Text(value));");
            w.Line();
            w.Open("private static ContractViolationException Undocumented(string operation, HttpResponseMessage response)");
            w.Line("return new(operation, (int)response.StatusCode, \"the spec does not document this status\");");
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
            w.Line("throw new ContractViolationException(operation, status, $\"the content type is '{actual}', the spec says '{mediaType}'\");");
            w.Close();
            w.Line();
            w.Line("T? value;");
            w.Open("try");
            w.Line("value = await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken).ConfigureAwait(false);");
            w.Close();
            w.Open("catch (JsonException e)");
            w.Line("throw new ContractViolationException(operation, status, \"the body does not match the schema: \" + e.Message, e);");
            w.Close();
            w.Line();
            w.Open("if (value is null)");
            w.Line("throw new ContractViolationException(operation, status, \"the body is null\");");
            w.Close();
            w.Line();
            w.Open("if (validate?.Invoke(value) is { } errors)");
            w.Line("throw new ContractViolationException(operation, status, \"the body breaks the schema: \"");
            w.Line("    + string.Join(\"; \", errors.Select(e => $\"{e.Key}: {string.Join(\" \", e.Value)}\")));");
            w.Close();
            w.Line();
            w.Line("return value;");
            w.Close();
            w.Line();
            w.Open("private void Observe(string operation, HttpResponseMessage response)");
            w.Line("_observed.TryAdd((operation, (int)response.StatusCode), 0);");
            w.Close();
            w.Close();
            return w.ToString();
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
                foreach (var p in query)
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

            if (operation.Body is not null)
            {
                w.Line("__request.Content = JsonContent.Create(body, options: Json);");
            }

            w.Line("using var __response = await _http.SendAsync(__request, cancellationToken).ConfigureAwait(false);");
            w.Open("switch ((int)__response.StatusCode)");

            foreach (var response in operation.Successes.Concat(operation.Errors))
            {
                var value = ClientRead(operation, response);
                EmitCase(w, operation, response, Outcome(operation, response, value), value is null);
            }

            w.Line("default:");
            w.Line($"    throw Undocumented({Naming.Literal(operation.Id)}, __response);");
            w.Close();
            w.Close();
        }

        /// <summary>
        /// The handler's return value for a documented response, given an expression for its body (null when it has none).
        /// Empty for an operation whose handler returns a plain Task. Shared by the client and the fake.
        /// </summary>
        private static string Outcome(Operation operation, Response response, string? value)
        {
            var hasErrors = operation.Errors.Count > 0;
            var successHasValue = operation.SuccessIsUnion || operation.Successes[0].BodyType is not null;
            if (operation.Errors.Contains(response))
            {
                var error = $"{operation.ErrorType}.{response.CaseName}({value ?? string.Empty})";
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
            w.Line($"case {response.Status.Invariant()}:");
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

            var validate = response.SchemaName is null
                ? "null"
                : response.IsList
                    ? $"items => ValidateEach(items, {ValidationClass}.Validate)"
                    : $"{ValidationClass}.Validate";
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
        /// a fake server that needs no code. Requires Symbolon.Fakes.
        /// </summary>
        private string EmitFake(List<Operation> operations)
        {
            var w = new CodeWriter(_namespace);
            w.Usings("System", "System.Collections.Concurrent", "System.Collections.Generic", "System.Linq", "System.Text.Json",
                "System.Text.Json.Nodes", "System.Text.Json.Serialization", "System.Threading", "System.Threading.Tasks",
                "Resultful", "Symbolon.Fakes");

            var interfaces = string.Join(", ", operations.Select(o => HandlerInterface(o.Tag)).Distinct());
            var name = _apiName + "Fake";
            w.Line("/// <summary>");
            w.Line($"/// A fake <c>{_apiName}</c>. Each operation answers with its first documented success (the spec's example, or a");
            w.Line("/// sample built to pass the schema) until scripted with <c>On{Operation}</c>, <see cref=\"Respond\"/> or");
            w.Line("/// <see cref=\"UseScenario\"/>. Only documented outcomes, with bodies that pass the schema, can be scripted.");
            w.Line("/// </summary>");
            w.Open($"public sealed class {name} : {interfaces}");
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
            w.Line("internal static object? Answer(string operation, int status, string? json)");
            w.Line("    => TryAnswer(operation, status, json, out var answer) ? answer : throw new ArgumentException($\"The spec documents no {status} response for '{operation}'.\");");
            w.Line();
            w.Line("/// <summary>The handler's return value for a documented response, built fresh; null for a plain Task.</summary>");
            w.Open("internal static bool TryAnswer(string operation, int status, string? json, out object? answer)");
            w.Open("switch ((operation, status))");
            foreach (var operation in operations)
            {
                foreach (var response in operation.Successes.Concat(operation.Errors))
                {
                    w.Line($"case ({Naming.Literal(operation.Id)}, {response.Status.Invariant()}):");
                    string? value = null;
                    if (response.BodyType is not null)
                    {
                        var validate = response.SchemaName is null
                            ? "null"
                            : response.IsList ? $"items => ValidateEach(items, {ValidationClass}.Validate)" : $"{ValidationClass}.Validate";
                        value = $"Body<{response.BodyType}>(json ?? {Naming.Literal(response.SampleJson ?? "null")}, {validate})";
                    }

                    var outcome = Outcome(operation, response, value);
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
                    var parameter = response.BodyType is null ? string.Empty : $"{Optional(response.BodyType)} body = null";
                    w.Open($"public {rule} {response.CaseName}({parameter})");
                    w.Line(response.BodyType is null ? "string? json = null;" : $"var json = {fake}.Serialize(body);");
                    w.Line($"_ = {fake}.Answer({Naming.Literal(operation.Id)}, {response.Status.Invariant()}, json);");
                    w.Line($"_rule.Add(() => {fake}.Answer({Naming.Literal(operation.Id)}, {response.Status.Invariant()}, json));");
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
                "Microsoft.AspNetCore.Routing", "Microsoft.Extensions.DependencyInjection", "Microsoft.Extensions.Logging", "Symbolon.Fakes");
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
            w.Line("private const string SessionKey = \"Symbolon.Fakes.Session\";");
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
            var args = operation.Parameters
                .Select(p => $"[{p.Attribute}(Name = {Naming.Literal(p.Name)})] {p.Type} {p.Identifier}")
                .ToList();
            if (operation.Body is not null)
            {
                args.Add($"[FromBody] {operation.Body.Type} body");
            }

            args.Add($"[FromServices] {HandlerInterface(operation.Tag)} __handlers");
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

            if (operation.Body is not null)
            {
                w.Line($"if ({ValidationClass}.Validate(body) is {{ }} __errors)");
                w.Line("{");
                w.Line("    return TypedResults.ValidationProblem(__errors);");
                w.Line("}");
                w.Line();
            }

            var invoke = $"await __handlers.{operation.Name}({string.Join(", ", call)}).ConfigureAwait(false)";
            var hasValue = operation.SuccessIsUnion || operation.Successes[0].BodyType is not null;

            if (operation.Errors.Count == 0)
            {
                if (!hasValue)
                {
                    w.Line($"{invoke};");
                    w.Line($"return {operation.Successes[0].Factory("_")};");
                }
                else
                {
                    w.Line($"var __value = {invoke};");
                    w.Line($"return {Success(operation, "__value", returnType)};");
                }
            }
            else
            {
                w.Line($"var __result = {invoke};");
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
