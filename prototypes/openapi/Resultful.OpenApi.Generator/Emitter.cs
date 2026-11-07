using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.OpenApi;

namespace Resultful.OpenApi.Generator
{
    /// <summary>
    /// Turns one OpenAPI document into C# source. Prototype scope: component schemas that are objects, string enums or
    /// discriminated oneOf unions; JSON bodies; path, query and header parameters.
    /// </summary>
    internal sealed class Emitter
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

        public Emitter(OpenApiDocument document, string ns, string apiName, Location location, bool describesItself = false)
        {
            _describesItself = describesItself;
            _document = document;
            _namespace = ns;
            _apiName = apiName;
            _location = location;
        }

        public List<Diagnostic> Diagnostics { get; } = new();

        private string ValidationClass => _apiName + "Validation";

        public Dictionary<string, string> Emit(bool contract = true, bool server = true)
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
            }

            if (server)
            {
                files["Endpoints"] = EmitEndpoints(operations);
                if (_describesItself)
                {
                    files["OpenApi"] = EmitOpenApi();
                }
            }

            return files;
        }

        // ---------------------------------------------------------------- the served OpenAPI document

        /// <summary>
        /// Configuration for ASP.NET Core's own OpenAPI document, so the server describes its schemas the way the spec
        /// does. It renames nothing the spec did not name and invents nothing: every rule below is driven by the spec's
        /// own unions and enums, and the round-trip test compares the result against the spec.
        /// </summary>
        private string EmitOpenApi()
        {
            var w = new CodeWriter(_namespace);
            w.Usings("System.Linq", "System.Threading.Tasks", "Microsoft.AspNetCore.OpenApi", "Microsoft.OpenApi");

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
            w.Line($"options.CreateSchemaReferenceId = type => type.Type.Namespace == {Naming.Literal(_namespace)}");
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

        private string EmitValidation(List<Operation> operations)
        {
            var w = new CodeWriter(_namespace);
            w.Usings("System.Collections.Generic", "System.Text.RegularExpressions");

            w.Line("/// <summary>");
            w.Line("/// Validation generated from the schema constraints (required, minLength, maxLength, pattern, minimum, maximum).");
            w.Line("/// The endpoints run it on request bodies; the client runs it on response bodies.");
            w.Line("/// </summary>");
            w.Open($"public static class {ValidationClass}");

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
            public Body(string type) => Type = type;

            public string Type { get; }
        }

        private sealed class Param
        {
            public Param(string name, string identifier, string type, string attribute)
            {
                Name = name;
                Identifier = identifier;
                Type = type;
                Attribute = attribute;
            }

            public string Name { get; }

            public string Identifier { get; }

            public string Type { get; }

            public string Attribute { get; }
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
                operation.Parameters.Add(new Param(parameter.Name!, Naming.Camel(parameter.Name!), type + nullable, attribute));
            }

            if (spec.RequestBody is { } requestBody)
            {
                if (JsonSchema(requestBody.Content) is not { } bodySchema || TypeOf(bodySchema, context + " request body") is not { } bodyType)
                {
                    Unsupported(context + " request body", "request bodies must be a JSON $ref");
                    return false;
                }

                operation.Body = new Body(bodyType);
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

                (status is >= 200 and < 300 ? operation.Successes : operation.Errors)
                    .Add(new Response(status, bodyType, mediaType, isValidation, schemaName) { IsList = isList });
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

            var hasErrors = operation.Errors.Count > 0;
            var successHasValue = operation.SuccessIsUnion || operation.Successes[0].BodyType is not null;
            foreach (var response in operation.Successes)
            {
                var value = ClientRead(operation, response);
                string result;
                if (operation.SuccessIsUnion)
                {
                    var union = $"{operation.SuccessType}.{response.CaseName}({value ?? string.Empty})";
                    result = hasErrors ? $"global::Resultful.Result.Ok<{operation.SuccessType}, {operation.ErrorType}>({union})" : union;
                }
                else if (successHasValue)
                {
                    result = hasErrors ? $"global::Resultful.Result.Ok<{response.BodyType}, {operation.ErrorType}>({value})" : value!;
                }
                else
                {
                    result = hasErrors ? $"global::Resultful.Result.Ok<{operation.ErrorType}>()" : string.Empty;
                }

                EmitCase(w, operation, response, result, value is null);
            }

            foreach (var response in operation.Errors)
            {
                var value = ClientRead(operation, response);
                var error = $"{operation.ErrorType}.{response.CaseName}({value ?? string.Empty})";
                var result = successHasValue
                    ? $"global::Resultful.Result.Fail<{operation.SuccessType}, {operation.ErrorType}>({error})"
                    : $"global::Resultful.Result.Fail<{operation.ErrorType}>({error})";
                EmitCase(w, operation, response, result, value is null);
            }

            w.Line("default:");
            w.Line($"    throw Undocumented({Naming.Literal(operation.Id)}, __response);");
            w.Close();
            w.Close();
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

        // ---------------------------------------------------------------- endpoints

        private string EmitEndpoints(List<Operation> operations)
        {
            var w = new CodeWriter(_namespace);
            w.Usings(
                "System.Threading",
                "System.Threading.Tasks",
                "Microsoft.AspNetCore.Builder",
                "Microsoft.AspNetCore.Http",
                "Microsoft.AspNetCore.Http.HttpResults",
                "Microsoft.AspNetCore.Mvc",
                "Microsoft.AspNetCore.Routing");

            w.Open($"public static class {_apiName}Endpoints");
            w.Line("/// <summary>Configures JSON the way the spec reads: numbers are numbers, and a oneOf discriminator may come anywhere.</summary>");
            w.Open($"public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection Add{_apiName}(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
            w.Open("return global::Microsoft.Extensions.DependencyInjection.HttpJsonServiceExtensions.ConfigureHttpJsonOptions(services, options =>", braces: false);
            w.Line("{");
            w.Line("    options.SerializerOptions.NumberHandling = global::System.Text.Json.Serialization.JsonNumberHandling.Strict;");
            w.Line("    options.SerializerOptions.AllowOutOfOrderMetadataProperties = true;");
            w.Line("});");
            w.Close();
            w.Line();
            w.Line("/// <summary>Maps every operation in the spec onto its handler interface, which must be registered in DI.</summary>");
            w.Open($"public static IEndpointRouteBuilder Map{_apiName}(this IEndpointRouteBuilder app)");

            foreach (var operation in operations)
            {
                EmitEndpoint(w, operation);
            }

            w.Line("return app;");
            w.Close();
            w.Line();
            w.Line("/// <summary>Writes a problem as application/problem+json with the status the spec gives it, whatever the handler set.</summary>");
            w.Open("private static ProblemHttpResult Problem(ProblemDetails problem, int status)");
            w.Line("problem.Status = status;");
            w.Line("return TypedResults.Problem(problem);");
            w.Close();
            w.Close();
            return w.ToString();
        }

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
