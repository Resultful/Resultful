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
    /// <summary>The emitter's fake (decision Q10: the model and its outputs, split by output).</summary>
    internal sealed partial class Emitter
    {
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
            w.Usings(OutcomeUsings("System", "System.Collections.Concurrent", "System.Collections.Generic", "System.Linq", "System.Text.Json",
                "System.Text.Json.Nodes", "System.Text.Json.Serialization", "System.Threading", "System.Threading.Tasks",
                "Matchmark.Fakes"));

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
                // Locals are prefixed, so a parameter called status, json or call (Stripe has them) doesn't clash.
                w.Line($"var __call = new FakeCall({Naming.Literal(operation.Id)}, new Dictionary<string, object?> {{ {string.Join(", ", recorded)} }});");
                w.Line("_calls.Enqueue(__call);");
                w.Open("if (_scenario is { } __scenario)");
                w.Line("var (__status, __json) = __scenario.Next(__call, Arguments(__call));");
                w.Line($"var __answer = Answer({Naming.Literal(operation.Id)}, __status, __json);");
                w.Line($"__scenario.Answered(__json ?? SampleJson({Naming.Literal(operation.Id)}, __status));");
                w.Line(operation.HandlerResultType is { } r
                    ? $"return Task.FromResult(({r})__answer!);"
                    : "return Task.CompletedTask;");
                w.Close();
                w.Line();
                w.Open("if (Rules.Find(__call) is { } __rule)");
                if (operation.HandlerResultType is { } rr)
                {
                    w.Line($"return Task.FromResult(({rr})__rule.Next()!);");
                }
                else
                {
                    w.Line("__rule.Next();");
                    w.Line("return Task.CompletedTask;");
                }

                w.Close();
                w.Line();
                w.Open("if (Rules.NothingElse)");
                w.Line("throw new FakeExpectationException($\"{FakeCallMatcher.Describe(__call)} matched no Given, and the fake was told to expect nothing else\");");
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
                    var sync = Callable($"{operation.Name}Answer", types.Append(result).ToList());
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
            foreach (var declaration in _delegates.Values)
            {
                w.Line();
                w.Line(declaration);
            }

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
                    var func = Callable($"{operation.Name}Where", args.Select(a => a.Type).Append("bool").ToList());
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
                    w.Line($"public {rule} {operation.Name}({Callable($"{operation.Name}Where", args.Select(a => a.Type).Append("bool").ToList())} where)");
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
                    w.Line($"public FakeCallAssertion {operation.Name}({Callable($"{operation.Name}Where", args.Select(a => a.Type).Append("bool").ToList())} where)");
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

        /// <summary>Delegates for callables too long for Func (more than 16 arguments), declared beside the fake.</summary>
        private readonly Dictionary<string, string> _delegates = new(StringComparer.Ordinal);

        /// <summary>A Func of these types (the last is the result), or a delegate of the fake's own when Func has too few type parameters.</summary>
        private string Callable(string name, List<string> types)
        {
            if (types.Count <= 17)
            {
                return $"Func<{string.Join(", ", types)}>";
            }

            var type = $"{_apiName}Fake{name}";
            _delegates[type] = $"public delegate {types[types.Count - 1]} {type}({string.Join(", ", types.Take(types.Count - 1).Select((t, i) => $"{t} arg{i.Invariant()}"))});";
            return type;
        }

        private string Responder(Operation operation)
        {
            var types = operation.Parameters.Select(p => p.Type).ToList();
            if (operation.Body is not null)
            {
                types.Add(operation.Body.Type);
            }

            types.Add("CancellationToken");
            types.Add(operation.HandlerReturnType);
            return Callable($"{operation.Name}Respond", types);
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
            w.Line("var started = Start(\"f\" + Guid.NewGuid().ToString(\"N\")[..12], scenario);");
            w.Line("return (started.Id, started.Fake);");
            w.Close();
            w.Line();
            w.Line("/// <summary>Starts (or restarts) the session the consumer names, as the session host does: the id must be a host label.</summary>");
            w.Open($"public (string Id, {fake} Fake, bool Created) Start(string id, Scenario? scenario)");
            w.Open("if (!System.Text.RegularExpressions.Regex.IsMatch(id, \"^[a-z][a-z0-9-]{0,62}$\"))");
            w.Line("throw new ArgumentException($\"'{id}' isn't a session id: use 1 to 63 lower-case letters, digits and hyphens, starting with a letter.\", nameof(id));");
            w.Close();
            w.Line();
            w.Line($"var fake = new {fake}();");
            w.Open("if (scenario is not null)");
            w.Line("fake.UseScenario(scenario);");
            w.Close();
            w.Line();
            w.Line("var created = !_sessions.ContainsKey(id);");
            w.Line("_sessions[id] = fake;");
            w.Line("return (id, fake, created);");
            w.Close();
            w.Line();
            w.Line("/// <summary>The session a request is for, as the session host routes: the first label of its host, X-Matchmark-Session, or null.</summary>");
            w.Open("public string? Route(HttpContext context)");
            w.Line("var host = context.Request.Host.Host;");
            w.Open("if (host.Contains('.') && host.Split('.')[0] is { } label && label != \"www\" && _sessions.ContainsKey(label))");
            w.Line("return label;");
            w.Close();
            w.Line();
            w.Line("return context.Request.Headers.TryGetValue(\"X-Matchmark-Session\", out var header) && header.ToString().Trim() is { Length: > 0 } named ? named : null;");
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
            w.Line("    // By host or header, as the session host routes; an unknown one named by header is a 404, never the default fake.");
            w.Line("    if (hub.Route(context) is { } routed && !context.Request.Path.StartsWithSegments(\"/__fake\"))");
            w.Line("    {");
            w.Line("        if (hub.Find(routed) is not { } byHost)");
            w.Line("        {");
            w.Line("            await Results.Problem($\"There is no fake session '{routed}'.\", statusCode: 404).ExecuteAsync(context);");
            w.Line("            return;");
            w.Line("        }");
            w.Line();
            w.Line("        context.Items[SessionKey] = byHost;");
            w.Line("    }");
            w.Line("    else if (context.Request.Path.StartsWithSegments(sessions, out var rest) && rest.HasValue)");
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
            w.Line("// The session host's protocol (docs/reference/sessions.md): the consumer names the session, and reaches it by host or header.");
            w.Line("group.MapPut(\"/sessions/{id}\", async (string id, HttpRequest request) =>");
            w.Line("{");
            w.Line("    var text = await new System.IO.StreamReader(request.Body).ReadToEndAsync();");
            w.Line("    Scenario? scenario;");
            w.Line("    try");
            w.Line("    {");
            w.Line("        var start = string.IsNullOrWhiteSpace(text) ? null : System.Text.Json.Nodes.JsonNode.Parse(text);");
            w.Line("        scenario = start?[\"scenario\"] is { } given ? Scenario.FromJson(given.ToJsonString()) : null;");
            w.Line("    }");
            w.Line("    catch (JsonException e)");
            w.Line("    {");
            w.Line("        return Results.Problem(\"The start isn't valid JSON: \" + e.Message, statusCode: 400);");
            w.Line("    }");
            w.Line();
            w.Line($"    var problems = scenario is null ? [] : {fake}.Check(scenario);");
            w.Line("    if (problems.Count > 0)");
            w.Line("    {");
            w.Line("        return Results.Problem(string.Join(\"\\n\", problems), title: \"The scenario breaks the spec\", statusCode: 400);");
            w.Line("    }");
            w.Line();
            w.Line("    try");
            w.Line("    {");
            w.Line("        var (_, _, created) = hub.Start(id, scenario);");
            w.Line("        var host = request.Host;");
            w.Line("        var name = System.Net.IPAddress.TryParse(host.Host, out _) ? \"localhost\" : host.Host;");
            w.Line("        var body = new { id, baseUrl = $\"{request.Scheme}://{id}.{name}{(host.Port is { } port ? \":\" + port : string.Empty)}/\", header = new { name = \"X-Matchmark-Session\", value = id } };");
            w.Line("        return created ? Results.Created($\"{prefix}/sessions/{id}\", body) : Results.Ok(body);");
            w.Line("    }");
            w.Line("    catch (ArgumentException e)");
            w.Line("    {");
            w.Line("        return Results.Problem(e.Message, statusCode: 400);");
            w.Line("    }");
            w.Line("});");
            w.Line("group.MapGet(\"/sessions/{id}\", (string id) => hub.Find(id) is { } fake ? Results.Ok(fake.Verify()) : Results.NotFound());");
            w.Line("group.MapDelete(\"/sessions/{id}\", (string id) => hub.End(id) ? Results.NoContent() : Results.NotFound());");
            w.Line("return app;");
            w.Close();
            w.Close();
            return w.ToString();
        }
    }
}
