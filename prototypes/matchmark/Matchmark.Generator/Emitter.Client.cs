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
    /// <summary>The emitter's client (decision Q10: the model and its outputs, split by output).</summary>
    internal sealed partial class Emitter
    {
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
    }
}
