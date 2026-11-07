using System.Globalization;
using System.Text.Json.Nodes;

namespace Matchmark.Testing;

/// <summary>Runs Arazzo workflows against the API, checking every response as any other.</summary>
internal sealed partial class RegressionRunner
{
    /// <summary>A guard against goto loops: no workflow makes more calls than this.</summary>
    private const int MaxSteps = 200;

    public async Task<WorkflowResult> RunArazzoAsync(ArazzoWorkflow workflow, CancellationToken cancellationToken)
    {
        var findings = new List<Finding>();
        var exchanges = new List<Exchange>();
        if (suite.ArazzoGaps(workflow) is { Count: > 0 } gaps)
        {
            return new WorkflowResult(workflow.Name, exchanges, findings, $"not run: {string.Join("; ", gaps)}");
        }

        var context = new ArazzoContext(Inputs(workflow));
        await RunStepsAsync(workflow, context, findings, exchanges, 0, cancellationToken).ConfigureAwait(false);
        return new WorkflowResult(workflow.Name, exchanges, Noticed(findings), null);
    }

    /// <summary>The workflow's inputs: those given in the options, the rest sampled from its inputs schema, free text made unique.</summary>
    private JsonObject Inputs(ArazzoWorkflow workflow)
    {
        var inputs = new JsonObject();
        if (workflow.Json["inputs"] is { } schema)
        {
            var schemas = workflow.Document.Schemas;
            var samples = new Samples(schemas, new SchemaValidator(schemas));
            if (samples.TrySample(schema, full: false, out var sample) && sample is JsonObject sampled)
            {
                inputs = samples.Unique(sampled, schema, 30_000 + _arazzoRuns++) as JsonObject ?? sampled;
            }
        }

        if (options.WorkflowInputs.TryGetValue(workflow.Id, out var given))
        {
            foreach (var (name, value) in given)
            {
                inputs[name] = value?.DeepClone();
            }
        }

        return inputs;
    }

    private int _arazzoRuns;

    private async Task<bool> RunStepsAsync(ArazzoWorkflow workflow, ArazzoContext context, List<Finding> findings, List<Exchange> exchanges, int depth,
        CancellationToken cancellationToken)
    {
        var steps = workflow.Steps;
        var index = 0;
        var attempts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var calls = 0; index < steps.Count && calls < MaxSteps; calls++)
        {
            var step = steps[index];
            var stepId = step["stepId"]!.GetValue<string>();
            var subject = $"{workflow.Name}, step {stepId}";
            bool passed;
            if (step["workflowId"]?.GetValue<string>() is { } nested)
            {
                // A workflow in this document, run as one step; its outputs are the step's.
                var inner = suite.NestedWorkflow(nested, workflow.Document)!;
                var innerContext = new ArazzoContext(Parameters(step, context, workflow.Document).Aggregate(new JsonObject(), (o, p) => { o[p.Name] = p.Value?.DeepClone(); return o; }));
                passed = depth < 8 && await RunStepsAsync(inner, innerContext, findings, exchanges, depth + 1, cancellationToken).ConfigureAwait(false);
                context.Current = innerContext.Current;
                context.Steps[stepId] = Outputs(step, innerContext.Outputs.DeepClone() as JsonObject ?? [], innerContext);
            }
            else
            {
                var operation = suite.ArazzoOperation(step, workflow.Document)!;
                var exchange = await SendAsync(StepRequest(step, operation, context, workflow.Document), cancellationToken).ConfigureAwait(false);
                exchanges.Add(exchange);
                context.Current = exchange;
                Response(operation, exchange, requestValid: true, subject, findings);
                passed = (step["successCriteria"] as JsonArray)?.OfType<JsonObject>().All(c => Criteria.Holds(c, context))
                         ?? exchange.Status is >= 200 and < 300;
                if (passed)
                {
                    context.Steps[stepId] = Outputs(step, [], context);
                }
            }

            var actions = (passed ? step["onSuccess"] : step["onFailure"]) as JsonArray ?? [];
            var action = actions.OfType<JsonObject>()
                .Select(a => a["reference"]?.GetValue<string>() is { } r ? workflow.Document.Component(r) as JsonObject ?? a : a)
                .FirstOrDefault(a => (a["criteria"] as JsonArray ?? []).OfType<JsonObject>().All(c => Criteria.Holds(c, context)));
            switch (action?["type"]?.GetValue<string>())
            {
                case "end":
                    Finish(workflow, context);
                    return passed;
                case "goto" when action["stepId"]?.GetValue<string>() is { } target:
                    index = steps.ToList().FindIndex(s => s["stepId"]!.GetValue<string>() == target);
                    continue;
                case "goto" when action["workflowId"]?.GetValue<string>() is { } other:
                    var next = suite.NestedWorkflow(other, workflow.Document)!;
                    return await RunStepsAsync(next, context, findings, exchanges, depth + 1, cancellationToken).ConfigureAwait(false);
                case "retry" when !passed:
                    attempts[stepId] = attempts.GetValueOrDefault(stepId) + 1;
                    if (attempts[stepId] <= (action["retryLimit"] is { } limit ? (int)(SchemaValidator.NumberOf(limit) ?? 1) : 1))
                    {
                        await WaitAsync(action, context, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    break;
            }

            if (!passed)
            {
                var unmet = (step["successCriteria"] as JsonArray ?? []).OfType<JsonObject>()
                    .Where(c => !Criteria.Holds(c, context))
                    .Select(c => c["condition"]?.GetValue<string>())
                    .ToList();
                var exchange = context.Current;
                findings.Add(new Finding(Checks.WorkflowCriteria, Severity.Failure, subject,
                    (unmet.Count > 0 ? $"not met: {string.Join("; ", unmet)}" : "the step failed")
                    + (exchange is null ? string.Empty : $" (answered {exchange.Status}: {RegressionReport.Excerpt(exchange.Body)})"),
                    exchange?.Request.Curl(client.BaseAddress) ?? string.Empty));
                return false;
            }

            index++;
        }

        Finish(workflow, context);
        return true;
    }

    private static void Finish(ArazzoWorkflow workflow, ArazzoContext context)
    {
        foreach (var (name, expression) in workflow.Json["outputs"]?.AsObject() ?? [])
        {
            context.Outputs[name] = context.Substitute(expression);
        }
    }

    private static JsonObject Outputs(JsonObject step, JsonObject outputs, ArazzoContext context)
    {
        foreach (var (name, expression) in step["outputs"]?.AsObject() ?? [])
        {
            outputs[name] = context.Substitute(expression);
        }

        return outputs;
    }

    /// <summary>Retry-After from the response when there is one (as Arazzo says), else retryAfter; capped by the options.</summary>
    private async Task WaitAsync(JsonObject action, ArazzoContext context, CancellationToken cancellationToken)
    {
        var seconds = context.Current?.Headers.TryGetValue("Retry-After", out var header) == true
                      && decimal.TryParse(header, NumberStyles.Float, CultureInfo.InvariantCulture, out var fromHeader)
            ? fromHeader
            : SchemaValidator.NumberOf(action["retryAfter"]) ?? 0;
        var wait = TimeSpan.FromSeconds((double)Math.Max(0, seconds));
        await Task.Delay(wait < options.MaxRetryWait ? wait : options.MaxRetryWait, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A step's parameters, each resolved (a <c>$components.parameters</c> reference, its value overridable) and substituted.</summary>
    private static IEnumerable<(string Name, string In, JsonNode? Value)> Parameters(JsonObject step, ArazzoContext context, ArazzoDocument document)
        => (step["parameters"] as JsonArray ?? []).OfType<JsonObject>()
            .Select(p => p["reference"]?.GetValue<string>() is { } r && document.Component(r) is JsonObject shared
                ? new JsonObject { ["name"] = shared["name"]?.DeepClone(), ["in"] = shared["in"]?.DeepClone(), ["value"] = (p["value"] ?? shared["value"])?.DeepClone() }
                : p)
            .Where(p => p["name"] is not null)
            .Select(p => (p["name"]!.GetValue<string>(), p["in"]?.GetValue<string>() ?? string.Empty, context.Substitute(p["value"])));

    /// <summary>The request a step makes: its parameters and body, expressions substituted; anything else required, sampled.</summary>
    private RegressionRequest StepRequest(JsonObject step, SpecOperation operation, ArazzoContext context, ArazzoDocument document)
    {
        var given = Parameters(step, context, document).ToList();
        var path = operation.Path;
        var pathParameters = new Dictionary<string, string>(StringComparer.Ordinal);
        var query = new List<KeyValuePair<string, string>>();
        var headers = new List<KeyValuePair<string, string>>();
        foreach (var parameter in operation.Parameters)
        {
            var match = given.FirstOrDefault(g => g.Name == parameter.Name && (g.In.Length == 0 || g.In == parameter.In));
            string? value = match.Name is not null ? ArazzoContext.Text(match.Value)
                : parameter.Required && suite.Samples.TrySample(parameter.Schema, false, out var sample) ? ArazzoContext.Text(sample)
                : null;
            if (value is null)
            {
                continue;
            }

            switch (parameter.In)
            {
                case "path":
                    path = path.Replace($"{{{parameter.Name}}}", Uri.EscapeDataString(value), StringComparison.Ordinal);
                    pathParameters[parameter.Name] = value;
                    break;
                case "query":
                    query.Add(new(parameter.Name, value));
                    break;
                case "header":
                    headers.Add(new(parameter.Name, value));
                    break;
            }
        }

        string? body = null;
        string? contentType = null;
        if (step["requestBody"] is JsonObject requestBody)
        {
            var payload = context.Substitute(requestBody["payload"]);
            foreach (var replacement in (requestBody["replacements"] as JsonArray ?? []).OfType<JsonObject>())
            {
                Replace(ref payload, replacement["target"]!.GetValue<string>(), context.Substitute(replacement["value"]));
            }

            contentType = requestBody["contentType"]?.GetValue<string>() ?? operation.Body?.MediaType ?? "application/json";
            body = payload is JsonValue text && text.GetValueKind() == System.Text.Json.JsonValueKind.String && !OpenApiSpec.IsJson(contentType)
                ? text.GetValue<string>()
                : payload?.ToJsonString() ?? "null";
        }
        else if (operation.Body is { Required: true } required && suite.Samples.TrySample(required.Schema, false, out var sampled))
        {
            body = suite.Samples.Unique(sampled, required.Schema, 40_000 + _arazzoRuns++)?.ToJsonString();
            contentType = required.MediaType;
        }

        return new RegressionRequest(operation.Method, path, query, headers, body, contentType) { PathParameters = pathParameters };
    }

    /// <summary>A replacement in a payload: the value at a JSON Pointer target, set.</summary>
    private static void Replace(ref JsonNode? payload, string target, JsonNode? value)
    {
        var pointer = target.TrimStart('#');
        if (pointer.Length == 0 || pointer == "/")
        {
            payload = value;
            return;
        }

        var parent = ArazzoContext.Pointer(payload, pointer[..pointer.LastIndexOf('/')]);
        var name = pointer[(pointer.LastIndexOf('/') + 1)..].Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
        switch (parent)
        {
            case JsonObject obj:
                obj[name] = value;
                break;
            case JsonArray array when int.TryParse(name, out var index) && index < array.Count:
                array[index] = value;
                break;
        }
    }
}
