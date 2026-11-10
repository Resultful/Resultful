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
    /// <summary>The emitter's operations (decision Q10: the model and its outputs, split by output).</summary>
    internal sealed partial class Emitter
    {
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

            public string SuccessType => SuccessIsUnion ? Name + "Success" : Successes[0].BodyType ?? UnitType;

            /// <summary>The type of a success with no body: the contract's own Unit, or Resultful's.</summary>
            public string UnitType { get; set; } = "global::Resultful.Unit";

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
                    var operation = new Operation(spec.OperationId!, method, path.Key, tag) { UnitType = Outcomes + ".Unit" };
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
                // Decided once, for every kind of body (the model's ModelBody.Required); text bodies read it for their minimum.
                operation.RequiredBody = requestBody.Required;
                if (JsonSchema(requestBody.Content) is { } bodySchema)
                {
                    // A shared request body (components/requestBodies) is named after itself, as a shared response is.
                    var requestHint = requestBody is OpenApiRequestBodyReference { Reference.Id: { } sharedBody } ? Naming.Pascal(sharedBody) + "Request" : operation.Name + "Request";
                    if (TypeOf(bodySchema, context + " request body", requestHint) is not { } bodyType)
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

                    // A shared response (components/responses) names its inline body after itself, the same wherever it's used,
                    // so the name doesn't depend on which operation is read first (or which tag's slice is generated).
                    var hint = pair.Value is OpenApiResponseReference { Reference.Id: { } shared }
                        ? $"{Naming.Pascal(shared)}Body"
                        : $"{operation.Name}{(status <= 0 ? new Response(status, null).CaseName : Naming.StatusNames[status])}Body";
                    bodyType = TypeOf(responseSchema, $"{context} response '{pair.Key}'", hint);
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
    }
}
