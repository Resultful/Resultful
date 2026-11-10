using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Matchmark.Specs.Arazzo
{
    /// <summary>An Arazzo document, read: its version, sources, workflows and components.</summary>
    public sealed class ArazzoDocument
    {
        public ArazzoDocument(string version, string? self, string title, IReadOnlyList<ArazzoSource> sources, IReadOnlyList<ArazzoWorkflow> workflows, JsonObject source)
        {
            Version = version;
            Self = self;
            Title = title;
            Sources = sources;
            Workflows = workflows;
            Source = source;
        }

        /// <summary>The <c>arazzo</c> field: 1.0.x or 1.1.x.</summary>
        public string Version { get; }

        /// <summary>1.1's <c>$self</c>.</summary>
        public string? Self { get; }

        public string Title { get; }

        public IReadOnlyList<ArazzoSource> Sources { get; }

        public IReadOnlyList<ArazzoWorkflow> Workflows { get; }

        /// <summary>The document as read, for what the model doesn't hold (inputs' schemas, components).</summary>
        public JsonObject Source { get; }
    }

    /// <summary>A source description: its name, URL, and type (<c>openapi</c>, <c>arazzo</c>, or 1.1's <c>asyncapi</c>).</summary>
    public sealed class ArazzoSource
    {
        public ArazzoSource(string name, string url, string? type)
        {
            Name = name;
            Url = url;
            Type = type;
        }

        public string Name { get; }

        public string Url { get; }

        public string? Type { get; }
    }

    public sealed class ArazzoWorkflow
    {
        public ArazzoWorkflow(string workflowId, IReadOnlyList<ArazzoStep> steps, IReadOnlyList<string> dependsOn)
        {
            WorkflowId = workflowId;
            Steps = steps;
            DependsOn = dependsOn;
        }

        public string WorkflowId { get; }

        public IReadOnlyList<ArazzoStep> Steps { get; }

        public IReadOnlyList<string> DependsOn { get; }
    }

    /// <summary>What a step calls: an OpenAPI operation, an AsyncAPI operation or channel (1.1), or another workflow.</summary>
    public enum StepKind
    {
        Operation,
        Message,
        Workflow,
    }

    public sealed class ArazzoStep
    {
        public ArazzoStep(string stepId, StepKind kind, string target, string? action, JsonObject source)
        {
            StepId = stepId;
            Kind = kind;
            Target = target;
            Action = action;
            Source = source;
        }

        public string StepId { get; }

        public StepKind Kind { get; }

        /// <summary>The operationId, operationPath, channelPath or workflowId the step names.</summary>
        public string Target { get; }

        /// <summary>An AsyncAPI step's <c>send</c> or <c>receive</c>.</summary>
        public string? Action { get; }

        public JsonObject Source { get; }
    }

    /// <summary>
    /// Reads an Arazzo document as the specification's text and schemas define it (specs/arazzo), for the version it
    /// declares: 1.0, or 1.1 (AsyncAPI steps and sources, <c>$self</c>, step <c>dependsOn</c>, selectors, expression types,
    /// workflow parameters to actions). Every problem is reported at its JSON pointer; a document with none is valid.
    /// </summary>
    public static class ArazzoReader
    {
        private static readonly string[] CriterionTypes = { "simple", "regex", "jsonpath", "xpath" };

        private static readonly Regex VersionPattern = new Regex(@"^1\.(?<minor>[01])\.\d+(-.+)?$", RegexOptions.CultureInvariant);
        private static readonly Regex SourceName = new Regex(@"^[A-Za-z0-9_\-]+$", RegexOptions.CultureInvariant);
        private static readonly Regex OutputName = new Regex(@"^[a-zA-Z0-9\.\-_]+$", RegexOptions.CultureInvariant);
        private static readonly Regex LocalStep = new Regex(@"^(?!\$).+$", RegexOptions.CultureInvariant | RegexOptions.Singleline);
        private static readonly Regex WorkflowStep = new Regex(@"^\$workflows\.[^.]+\.steps\.[^.]+$", RegexOptions.CultureInvariant);
        private static readonly Regex SourceStep = new Regex(@"^\$sourceDescriptions\.[^.]+\.[^.]+\.steps\.[^.]+$", RegexOptions.CultureInvariant);

        public static ReadResult<ArazzoDocument> Read(string text, string path) => Read(Documents.Load(text, path));

        public static ReadResult<ArazzoDocument> Read(JsonNode? node)
        {
            var problems = new List<Problem>();
            var reader = new Checks(problems);
            if (reader.Shape.Object(node, string.Empty, "An Arazzo document") is not { } root)
            {
                return new ReadResult<ArazzoDocument>(null, problems);
            }

            var version = reader.Shape.String(root, "arazzo", string.Empty, required: true);
            if (version is not null)
            {
                if (VersionPattern.Match(version) is { Success: true } match)
                {
                    reader.Minor = match.Groups["minor"].Value[0] - '0';
                }
                else
                {
                    reader.Shape.Add("/arazzo", $"'{version}' isn't an Arazzo version this reader knows: 1.0.x or 1.1.x.");
                }
            }

            var v11 = reader.Minor >= 1;
            reader.Shape.Only(root, string.Empty, "An Arazzo document", v11 ? ["arazzo", "$self", "info", "sourceDescriptions", "workflows", "components"] : ["arazzo", "info", "sourceDescriptions", "workflows", "components"]);
            string? self = null;
            if (v11 && (self = reader.Shape.String(root, "$self", string.Empty)) is not null && self.Contains("#"))
            {
                reader.Shape.Add("/$self", "'$self' refers to a whole document, so it can't have a fragment ('#').");
            }

            var title = reader.Info(root);
            var sources = reader.List(root, "sourceDescriptions", string.Empty, required: true, minimum: 1, (item, pointer) => reader.Source(item, pointer));
            var workflows = reader.List(root, "workflows", string.Empty, required: true, minimum: 1, (item, pointer) => reader.Workflow(item, pointer));
            if (root.TryGetPropertyValue("components", out var components))
            {
                reader.Components(components, "/components");
            }

            var document = version is null || title is null || sources is null || workflows is null
                ? null
                : new ArazzoDocument(version, self, title, sources, workflows, root);
            return new ReadResult<ArazzoDocument>(document, problems);
        }

        /// <summary>The checks, for the version read: 1.0 (<c>Minor</c> 0) or 1.1.</summary>
        private sealed class Checks
        {
            public Checks(List<Problem> problems) => Shape = new Shape(problems);

            public Shape Shape { get; }

            public int Minor { get; set; } = -1;

            private bool V11 => Minor >= 1;

            public string? Info(JsonObject root)
            {
                if (!root.TryGetPropertyValue("info", out var node))
                {
                    Shape.Add(string.Empty, "'info' is required.");
                    return null;
                }

                if (Shape.Object(node, "/info", "'info'") is not { } info)
                {
                    return null;
                }

                Shape.Only(info, "/info", "The Info Object", "title", "summary", "description", "version");
                Shape.String(info, "summary", "/info");
                Shape.String(info, "description", "/info");
                var version = Shape.String(info, "version", "/info", required: true);
                var title = Shape.String(info, "title", "/info", required: true);
                return version is null ? null : title;
            }

            /// <summary>An array field of unique items, each read by <paramref name="item"/>; null when it's missing or isn't one.</summary>
            public List<T>? List<T>(JsonObject owner, string field, string pointer, bool required, int minimum, Func<JsonNode?, string, T?> item)
                where T : class
            {
                if (!owner.TryGetPropertyValue(field, out var node))
                {
                    if (required)
                    {
                        Shape.Add(pointer, $"'{field}' is required.");
                    }

                    return null;
                }

                var at = Pointer.Join(pointer, field);
                if (node is not JsonArray array)
                {
                    Shape.Add(at, $"'{field}' must be an array.");
                    return null;
                }

                if (array.Count < minimum)
                {
                    Shape.Add(at, $"'{field}' must hold at least {minimum}.");
                }

                Unique(array, at, field);
                var read = new List<T>();
                for (var i = 0; i < array.Count; i++)
                {
                    if (item(array[i], Pointer.Join(at, i)) is { } value)
                    {
                        read.Add(value);
                    }
                }

                return read;
            }

            private void Unique(JsonArray array, string pointer, string field)
            {
                for (var i = 1; i < array.Count; i++)
                {
                    for (var j = 0; j < i; j++)
                    {
                        if (JsonNode.DeepEquals(array[i], array[j]))
                        {
                            Shape.Add(Pointer.Join(pointer, i), $"'{field}' repeats item {j}: each must be unique.");
                            j = i;
                        }
                    }
                }
            }

            public ArazzoSource? Source(JsonNode? node, string pointer)
            {
                if (Shape.Object(node, pointer, "A Source Description Object") is not { } source)
                {
                    return null;
                }

                Shape.Only(source, pointer, "A Source Description Object", "name", "url", "type");
                var name = Shape.String(source, "name", pointer, required: true);
                if (name is not null && !SourceName.IsMatch(name))
                {
                    Shape.Add(Pointer.Join(pointer, "name"), $"'{name}' isn't a source name: letters, digits, '_' and '-' only.");
                }

                var url = Shape.String(source, "url", pointer, required: true);
                var type = Enum(source, "type", pointer, V11 ? ["arazzo", "openapi", "asyncapi"] : ["arazzo", "openapi"]);
                return name is null || url is null ? null : new ArazzoSource(name, url, type);
            }

            /// <summary>A string field that must be one of <paramref name="values"/>.</summary>
            private string? Enum(JsonObject owner, string field, string pointer, params string[] values)
            {
                if (!owner.TryGetPropertyValue(field, out var node))
                {
                    return null;
                }

                if (node is JsonValue v && v.GetValueKind() == JsonValueKind.String && Array.IndexOf(values, v.GetValue<string>()) >= 0)
                {
                    return v.GetValue<string>();
                }

                Shape.Add(Pointer.Join(pointer, field), $"'{field}' must be one of {string.Join(", ", values)}.");
                return null;
            }

            private void Integer(JsonObject owner, string field, string pointer, bool nonNegative)
            {
                if (owner.TryGetPropertyValue(field, out var node)
                    && !(JsonNumber.Decimal(node) is { } d && d == decimal.Truncate(d) && (!nonNegative || d >= 0)))
                {
                    Shape.Add(Pointer.Join(pointer, field), $"'{field}' must be {(nonNegative ? "a non-negative " : "an ")}integer.");
                }
            }

            private void Number(JsonObject owner, string field, string pointer)
            {
                if (owner.TryGetPropertyValue(field, out var node) && !(JsonNumber.Decimal(node) is { } n && n >= 0))
                {
                    Shape.Add(Pointer.Join(pointer, field), $"'{field}' must be a non-negative number.");
                }
            }

            public ArazzoWorkflow? Workflow(JsonNode? node, string pointer)
            {
                if (Shape.Object(node, pointer, "A Workflow Object") is not { } workflow)
                {
                    return null;
                }

                Shape.Only(workflow, pointer, "A Workflow Object", "workflowId", "summary", "description", "inputs", "dependsOn", "steps", "successActions", "failureActions", "outputs", "parameters");
                var id = Shape.String(workflow, "workflowId", pointer, required: true);
                Shape.String(workflow, "summary", pointer);
                Shape.String(workflow, "description", pointer);
                if (workflow.TryGetPropertyValue("inputs", out var inputs))
                {
                    Schema(inputs, Pointer.Join(pointer, "inputs"));
                }

                var dependsOn = List(workflow, "dependsOn", pointer, required: false, minimum: 0, (item, at) => StringItem(item, at, "dependsOn")) ?? new List<string>();
                var steps = List(workflow, "steps", pointer, required: true, minimum: 1, (item, at) => Step(item, at));
                List(workflow, "successActions", pointer, required: false, minimum: 0, (item, at) => ActionOrReusable(item, at, success: true));
                List(workflow, "failureActions", pointer, required: false, minimum: 0, (item, at) => ActionOrReusable(item, at, success: false));
                Outputs(workflow, pointer);
                List(workflow, "parameters", pointer, required: false, minimum: 0, (item, at) => ParameterOrReusable(item, at, needsIn: false));
                return id is null || steps is null ? null : new ArazzoWorkflow(id, steps, dependsOn);
            }

            private string? StringItem(JsonNode? item, string pointer, string field)
            {
                if (item is JsonValue v && v.GetValueKind() == JsonValueKind.String)
                {
                    return v.GetValue<string>();
                }

                Shape.Add(pointer, $"Each of '{field}' must be a string.");
                return null;
            }

            /// <summary>A JSON Schema 2020-12, well formed as its metaschema judges it.</summary>
            private void Schema(JsonNode? node, string pointer) => JsonSchemaShape.Check(Shape, node, pointer);

            private void Outputs(JsonObject owner, string pointer)
            {
                if (!owner.TryGetPropertyValue("outputs", out var node))
                {
                    return;
                }

                var at = Pointer.Join(pointer, "outputs");
                if (Shape.Object(node, at, "'outputs'") is not { } outputs)
                {
                    return;
                }

                // Keys that match the pattern are checked; the schema leaves other keys alone.
                foreach (var pair in outputs.Where(p => OutputName.IsMatch(p.Key)))
                {
                    var key = Pointer.Join(at, pair.Key);
                    if (pair.Value is JsonValue v && v.GetValueKind() == JsonValueKind.String)
                    {
                        continue;
                    }

                    if (V11 && pair.Value is JsonObject)
                    {
                        Selector(pair.Value, key);
                    }
                    else
                    {
                        Shape.Add(key, V11 ? "An output must be a runtime expression (a string) or a Selector Object." : "An output must be a runtime expression (a string).");
                    }
                }
            }

            public ArazzoStep? Step(JsonNode? node, string pointer)
            {
                if (Shape.Object(node, pointer, "A Step Object") is not { } step)
                {
                    return null;
                }

                // Which step it is: a workflow step names a workflowId; a 1.1 AsyncAPI step has an action; any other calls an
                // OpenAPI operation. The schema's alternatives are disjoint in exactly these fields.
                var kind = step.ContainsKey("workflowId") ? StepKind.Workflow : V11 && step.ContainsKey("action") ? StepKind.Message : StepKind.Operation;
                var common = new List<string> { "stepId", "description", "parameters", "requestBody", "successCriteria", "onSuccess", "onFailure", "outputs" };
                if (V11)
                {
                    common.AddRange(["timeout", "dependsOn"]);
                }

                var own = kind switch
                {
                    StepKind.Workflow => new[] { "workflowId" },
                    StepKind.Message => new[] { "operationId", "channelPath", "correlationId", "action" },
                    _ => new[] { "operationId", "operationPath" },
                };
                var what = kind switch { StepKind.Workflow => "A workflow step", StepKind.Message => "An AsyncAPI step", _ => "An OpenAPI step" };
                Shape.Only(step, pointer, what, [.. common, .. own]);

                var id = Shape.String(step, "stepId", pointer, required: true);
                Shape.String(step, "description", pointer);
                if (V11)
                {
                    Integer(step, "timeout", pointer, nonNegative: false);
                    StepDependsOn(step, pointer);
                }

                string? target;
                string? action = null;
                switch (kind)
                {
                    case StepKind.Workflow:
                        target = Shape.String(step, "workflowId", pointer, required: true);
                        break;
                    case StepKind.Message:
                        action = Enum(step, "action", pointer, "send", "receive");
                        target = ExactlyOne(step, pointer, "operationId", "channelPath");
                        if (step.ContainsKey("correlationId"))
                        {
                            if (step["correlationId"] is null)
                            {
                                Shape.Add(Pointer.Join(pointer, "correlationId"), "'correlationId' can't be null.");
                            }

                            if (action is not null && action != "receive")
                            {
                                Shape.Add(Pointer.Join(pointer, "correlationId"), "'correlationId' is only for a step that receives.");
                            }
                        }

                        break;
                    default:
                        target = ExactlyOne(step, pointer, "operationId", "operationPath");
                        break;
                }

                List(step, "parameters", pointer, required: false, minimum: 0, (item, at) => ParameterOrReusable(item, at, needsIn: kind != StepKind.Workflow));
                if (step.TryGetPropertyValue("requestBody", out var body))
                {
                    RequestBody(body, Pointer.Join(pointer, "requestBody"));
                }

                List(step, "successCriteria", pointer, required: false, minimum: 1, (item, at) => Criterion(item, at));
                List(step, "onSuccess", pointer, required: false, minimum: 0, (item, at) => ActionOrReusable(item, at, success: true));
                List(step, "onFailure", pointer, required: false, minimum: 0, (item, at) => ActionOrReusable(item, at, success: false));
                Outputs(step, pointer);
                return id is null || target is null ? null : new ArazzoStep(id, kind, target, action, step);
            }

            /// <summary>Exactly one of two fields, a string: the one given.</summary>
            private string? ExactlyOne(JsonObject owner, string pointer, string first, string second)
            {
                var a = Shape.String(owner, first, pointer);
                var b = Shape.String(owner, second, pointer);
                if (owner.ContainsKey(first) == owner.ContainsKey(second))
                {
                    Shape.Add(pointer, owner.ContainsKey(first) ? $"A step has '{first}' or '{second}', not both." : $"A step needs '{first}' or '{second}'.");
                    return null;
                }

                return a ?? b;
            }

            private void StepDependsOn(JsonObject step, string pointer)
            {
                List(step, "dependsOn", pointer, required: false, minimum: 1, (item, at) =>
                {
                    if (StringItem(item, at, "dependsOn") is { } value && !(LocalStep.IsMatch(value) || WorkflowStep.IsMatch(value) || SourceStep.IsMatch(value)))
                    {
                        Shape.Add(at, $"'{value}' isn't a step: a stepId, $workflows.<workflowId>.steps.<stepId>, or $sourceDescriptions.<name>.<workflowId>.steps.<stepId>.");
                    }

                    return string.Empty;
                });
            }

            /// <summary>A Reusable Object (it has <c>reference</c>) or a Parameter Object; an operation's parameters need <c>in</c>.</summary>
            private object? ParameterOrReusable(JsonNode? node, string pointer, bool needsIn)
            {
                if (node is JsonObject obj && obj.ContainsKey("reference"))
                {
                    Reusable(obj, pointer, valueTypes: true);
                    return obj;
                }

                if (Parameter(node, pointer) is { } parameter && needsIn && !parameter.ContainsKey("in"))
                {
                    Shape.Add(pointer, "An operation's parameter needs 'in'.");
                }

                return node;
            }

            /// <summary>A Reusable Object: <c>reference</c>, and a <c>value</c> where a parameter is referred to; nothing else, extensions included.</summary>
            private void Reusable(JsonObject reusable, string pointer, bool valueTypes)
            {
                foreach (var field in reusable.Select(p => p.Key).Where(k => k is not ("reference" or "value")))
                {
                    Shape.Add(Pointer.Join(pointer, field), $"A Reusable Object has no field '{field}'.");
                }

                Shape.String(reusable, "reference", pointer, required: true);
            }

            private JsonObject? Parameter(JsonNode? node, string pointer)
            {
                if (Shape.Object(node, pointer, "A Parameter Object") is not { } parameter)
                {
                    return null;
                }

                Shape.Only(parameter, pointer, "A Parameter Object", "name", "in", "value");
                Shape.String(parameter, "name", pointer, required: true);
                Enum(parameter, "in", pointer, V11 ? ["path", "query", "querystring", "header", "cookie", "channel"] : ["path", "query", "header", "cookie"]);
                if (!parameter.TryGetPropertyValue("value", out var value))
                {
                    Shape.Add(pointer, "'value' is required.");
                }
                else if (V11 && value is JsonObject)
                {
                    // 1.1: a parameter's value is a literal that isn't an object, or a Selector Object.
                    Selector(value, Pointer.Join(pointer, "value"));
                }

                return parameter;
            }

            private void RequestBody(JsonNode? node, string pointer)
            {
                if (Shape.Object(node, pointer, "A Request Body Object") is not { } body)
                {
                    return;
                }

                Shape.Only(body, pointer, "A Request Body Object", "contentType", "payload", "replacements");
                Shape.String(body, "contentType", pointer);
                List(body, "replacements", pointer, required: false, minimum: 0, (item, at) =>
                {
                    if (Shape.Object(item, at, "A Payload Replacement Object") is not { } replacement)
                    {
                        return null;
                    }

                    Shape.Only(replacement, at, "A Payload Replacement Object", V11 ? ["target", "targetSelectorType", "value"] : ["target", "value"]);
                    Shape.String(replacement, "target", at, required: true);
                    if (V11 && replacement.TryGetPropertyValue("targetSelectorType", out var selectorType))
                    {
                        SelectorType(selectorType, Pointer.Join(at, "targetSelectorType"));
                    }

                    if (!replacement.ContainsKey("value"))
                    {
                        Shape.Add(at, "'value' is required.");
                    }
                    else if (!V11)
                    {
                        Shape.String(replacement, "value", at);
                    }

                    return replacement;
                });
            }

            /// <summary>A Criterion Object: a condition, its context, and the type of condition (and, 1.0, an expression type on it).</summary>
            private JsonObject? Criterion(JsonNode? node, string pointer)
            {
                if (Shape.Object(node, pointer, "A Criterion Object") is not { } criterion)
                {
                    return null;
                }

                Shape.String(criterion, "condition", pointer, required: true);
                Shape.String(criterion, "context", pointer);
                if (criterion.ContainsKey("type") && !criterion.ContainsKey("context"))
                {
                    Shape.Add(pointer, "A criterion with a 'type' needs a 'context'.");
                }

                criterion.TryGetPropertyValue("type", out var type);
                if (V11)
                {
                    Shape.Only(criterion, pointer, "A Criterion Object", "context", "condition", "type");
                    if (type is JsonObject)
                    {
                        ExpressionType(type, Pointer.Join(pointer, "type"), ["jsonpath", "xpath", "jsonpointer"]);
                    }
                    else if (criterion.ContainsKey("type"))
                    {
                        Enum(criterion, "type", pointer, "simple", "regex", "jsonpath", "xpath");
                    }

                    return criterion;
                }

                // 1.0: the type is one of the names; or the criterion is itself an expression type, with a version.
                var named = !criterion.ContainsKey("type") || type is JsonValue v && v.GetValueKind() == JsonValueKind.String
                            && Array.IndexOf(CriterionTypes, v.GetValue<string>()) >= 0;
                var typed = criterion.ContainsKey("type") && criterion.ContainsKey("version") && Version10(criterion);
                if (!named && !typed)
                {
                    Shape.Add(Pointer.Join(pointer, "type"), "'type' must be simple, regex, jsonpath or xpath (or jsonpath or xpath with a 'version').");
                }

                Shape.Only(criterion, pointer, "A Criterion Object", typed ? ["context", "condition", "type", "version"] : ["context", "condition", "type"]);
                return criterion;
            }

            /// <summary>1.0's criterion expression type, on the criterion: jsonpath (draft-goessner-dispatch-jsonpath-00) or xpath (10, 20, 30).</summary>
            private static bool Version10(JsonObject criterion)
            {
                if (criterion["type"] is not JsonValue t || t.GetValueKind() != JsonValueKind.String || criterion["version"] is not JsonValue v || v.GetValueKind() != JsonValueKind.String)
                {
                    return false;
                }

                return (t.GetValue<string>(), v.GetValue<string>()) switch
                {
                    ("jsonpath", "draft-goessner-dispatch-jsonpath-00") => true,
                    ("xpath", "xpath-10" or "xpath-20" or "xpath-30") => true,
                    _ => false,
                };
            }

            /// <summary>1.1's Expression Type Object: a type and its version, from the specification's lists.</summary>
            private void ExpressionType(JsonNode? node, string pointer, string[] types)
            {
                if (Shape.Object(node, pointer, "An Expression Type Object") is not { } expression)
                {
                    return;
                }

                var type = Enum(expression, "type", pointer, types);
                if (!expression.ContainsKey("type"))
                {
                    Shape.Add(pointer, "'type' is required.");
                }

                var version = Shape.String(expression, "version", pointer, required: true);
                var allowed = type switch
                {
                    "jsonpath" => new[] { "rfc9535", "draft-goessner-dispatch-jsonpath-00" },
                    "xpath" => new[] { "xpath-10", "xpath-20", "xpath-30", "xpath-31" },
                    "jsonpointer" => new[] { "rfc6901" },
                    _ => null,
                };
                if (version is not null && allowed is not null && Array.IndexOf(allowed, version) < 0)
                {
                    Shape.Add(Pointer.Join(pointer, "version"), $"A {type} expression's version is one of {string.Join(", ", allowed)}.");
                }
            }

            private void SelectorType(JsonNode? node, string pointer)
            {
                if (node is JsonObject)
                {
                    ExpressionType(node, pointer, ["jsonpath", "xpath", "jsonpointer"]);
                }
                else if (!(node is JsonValue v && v.GetValueKind() == JsonValueKind.String && v.GetValue<string>() is "jsonpointer" or "jsonpath" or "xpath"))
                {
                    Shape.Add(pointer, "A selector type is jsonpointer, jsonpath or xpath, or an Expression Type Object.");
                }
            }

            /// <summary>1.1's Selector Object: a context, a selector, and its type.</summary>
            private void Selector(JsonNode? node, string pointer)
            {
                if (Shape.Object(node, pointer, "A Selector Object") is not { } selector)
                {
                    return;
                }

                Shape.Only(selector, pointer, "A Selector Object", "context", "selector", "type");
                Shape.String(selector, "context", pointer, required: true);
                Shape.String(selector, "selector", pointer, required: true);
                if (!selector.TryGetPropertyValue("type", out var type))
                {
                    Shape.Add(pointer, "'type' is required.");
                }
                else
                {
                    SelectorType(type, Pointer.Join(pointer, "type"));
                }
            }

            private JsonObject? ActionOrReusable(JsonNode? node, string pointer, bool success)
            {
                if (node is JsonObject obj && obj.ContainsKey("reference"))
                {
                    Reusable(obj, pointer, valueTypes: true);
                    return obj;
                }

                return Action(node, pointer, success);
            }

            /// <summary>A Success or Failure Action Object: <c>end</c>, <c>goto</c> (a workflow or a step), or <c>retry</c>.</summary>
            private JsonObject? Action(JsonNode? node, string pointer, bool success)
            {
                var what = success ? "A Success Action Object" : "A Failure Action Object";
                if (Shape.Object(node, pointer, what) is not { } action)
                {
                    return null;
                }

                var fields = new List<string> { "name", "type", "workflowId", "stepId", "criteria" };
                if (!success)
                {
                    fields.AddRange(["retryAfter", "retryLimit"]);
                }

                if (V11)
                {
                    fields.Add("parameters");
                }

                Shape.Only(action, pointer, what, [.. fields]);
                Shape.String(action, "name", pointer, required: true);
                if (!action.ContainsKey("type"))
                {
                    Shape.Add(pointer, "'type' is required.");
                }

                var type = Enum(action, "type", pointer, success ? ["end", "goto"] : ["end", "goto", "retry"]);
                Shape.String(action, "workflowId", pointer);
                Shape.String(action, "stepId", pointer);
                var jumps = type == "goto" || (!V11 && !success && type == "retry");
                if (jumps && action.ContainsKey("workflowId") == action.ContainsKey("stepId"))
                {
                    Shape.Add(pointer, $"A {type} action names a 'workflowId' or a 'stepId', exactly one.");
                }

                if (!success)
                {
                    Number(action, "retryAfter", pointer);
                    Integer(action, "retryLimit", pointer, nonNegative: true);
                    if (!V11 && type == "retry" && !action.ContainsKey("retryAfter"))
                    {
                        Shape.Add(pointer, "A retry action needs 'retryAfter'.");
                    }
                }

                if (V11 && action.ContainsKey("parameters"))
                {
                    if (!action.ContainsKey("workflowId"))
                    {
                        Shape.Add(Pointer.Join(pointer, "parameters"), "An action passes parameters only to a workflow: it needs 'workflowId'.");
                    }

                    List(action, "parameters", pointer, required: false, minimum: 0, (item, at) => item);
                }

                List(action, "criteria", pointer, required: false, minimum: success ? 1 : 0, (item, at) => Criterion(item, at));
                return action;
            }

            public void Components(JsonNode? node, string pointer)
            {
                if (Shape.Object(node, pointer, "'components'") is not { } components)
                {
                    return;
                }

                Shape.Only(components, pointer, "The Components Object", "inputs", "parameters", "successActions", "failureActions");
                foreach (var field in new[] { "inputs", "parameters", "successActions", "failureActions" })
                {
                    if (!components.TryGetPropertyValue(field, out var section))
                    {
                        continue;
                    }

                    var at = Pointer.Join(pointer, field);
                    if (Shape.Object(section, at, $"'components.{field}'") is not { } entries)
                    {
                        continue;
                    }

                    foreach (var pair in entries)
                    {
                        var key = Pointer.Join(at, pair.Key);
                        if (!OutputName.IsMatch(pair.Key))
                        {
                            Shape.Add(key, $"'{pair.Key}' isn't a component name: letters, digits, '.', '-' and '_' only.");
                        }

                        switch (field)
                        {
                            case "inputs":
                                Schema(pair.Value, key);
                                break;
                            case "parameters":
                                Parameter(pair.Value, key);
                                break;
                            default:
                                Action(pair.Value, key, success: field == "successActions");
                                break;
                        }
                    }
                }
            }
        }
    }
}
