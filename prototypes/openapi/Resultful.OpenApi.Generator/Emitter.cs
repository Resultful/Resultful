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

        public Emitter(OpenApiDocument document, string ns, string apiName, Location location)
        {
            _document = document;
            _namespace = ns;
            _apiName = apiName;
            _location = location;
        }

        public List<Diagnostic> Diagnostics { get; } = new();

        private string ValidationClass => _apiName + "Validation";

        public Dictionary<string, string> Emit()
        {
            CollectSchemas();
            var operations = CollectOperations();

            return new Dictionary<string, string>
            {
                ["Models"] = EmitModels(),
                ["Validation"] = EmitValidation(operations),
                ["Responses"] = EmitResponses(operations),
                ["Handlers"] = EmitHandlers(operations),
                ["Endpoints"] = EmitEndpoints(operations),
            };
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

            w.Line("/// <summary>Request validation generated from the schema constraints (required, minLength, maxLength, pattern, minimum, maximum).</summary>");
            w.Open($"internal static class {ValidationClass}");

            foreach (var bodyType in operations.Where(o => o.Body is not null).Select(o => o.Body!.Type).Distinct())
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
            public Response(int status, string? bodyType)
            {
                Status = status;
                BodyType = bodyType;
            }

            public int Status { get; }

            public string? BodyType { get; }

            public string CaseName => Naming.StatusNames[Status];

            /// <summary>The <c>Microsoft.AspNetCore.Http.HttpResults</c> type this response is written as.</summary>
            public string ResultType => Status == 401
                ? "UnauthorizedHttpResult"
                : BodyType is null ? CaseName : $"{CaseName}<{BodyType}>";

            /// <summary>The TypedResults call that produces it, given the body expression.</summary>
            public string Factory(string body) => (Status, BodyType is null) switch
            {
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

            /// <summary>A 400 whose body is HttpValidationProblemDetails: owned by the generated boundary, not the handler.</summary>
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
                    return types;
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

                string? bodyType = null;
                if (JsonSchema(pair.Value.Content) is { } responseSchema)
                {
                    if (operation.Body is not null && status == 400 && RefId(responseSchema) == ValidationSchema)
                    {
                        operation.ValidationDocumented = true;
                        continue;
                    }

                    bodyType = TypeOf(responseSchema, $"{context} response '{pair.Key}'");
                    if (bodyType is null)
                    {
                        return false;
                    }
                }

                (status is >= 200 and < 300 ? operation.Successes : operation.Errors).Add(new Response(status, bodyType));
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
        {
            if (content is null)
            {
                return null;
            }

            foreach (var pair in content)
            {
                if (pair.Key.StartsWith("application/", StringComparison.OrdinalIgnoreCase)
                    && pair.Key.EndsWith("json", StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Value.Schema;
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
            }

            return w.ToString();
        }

        private static string HandlerInterface(string tag) => $"I{Naming.Pascal(tag)}Handlers";

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
            foreach (var problem in operation.Errors.Where(e => e.BodyType is { } t && WellKnownSchemas.ContainsValue(t)))
            {
                // TypedResults document every body as application/json; ProblemDetails is written as problem+json.
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
