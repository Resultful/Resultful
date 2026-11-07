using System.Text.Json.Nodes;

namespace Matchmark.Fakes;

/// <summary>A call the scenario did not expect: out of order, not matching its step, or beyond the last step.</summary>
public sealed class FakeExpectationException(string message) : Exception(message);

/// <summary>The outcome of a scenario: whether every step was called as written and nothing else was.</summary>
public sealed record ScenarioReport(bool Passed, IReadOnlyList<string> Failures, IReadOnlyList<FakeCall> Calls);

/// <summary>Runs a <see cref="Scenario"/>: answers each call from its matching step, and records every failure.</summary>
public sealed class ScenarioRunner(Scenario scenario)
{
    private readonly object _gate = new();
    private readonly int[] _used = new int[scenario.Steps.Count];
    private readonly List<string> _failures = [];
    private readonly List<FakeCall> _calls = [];
    private readonly Dictionary<string, JsonObject> _outputs = new(StringComparer.Ordinal);
    private int _last = -1;

    public Scenario Scenario { get; } = scenario;

    /// <summary>
    /// The answer (status, and body JSON or null for the default) for a call whose arguments are given as JSON.
    /// Throws <see cref="FakeExpectationException"/>, and records the failure, if the scenario doesn't expect it.
    /// </summary>
    public (int Status, string? Body) Next(FakeCall call, JsonObject arguments)
    {
        lock (_gate)
        {
            _calls.Add(call);
            var index = Scenario.Order == ScenarioOrder.InOrder ? NextInOrder(call, arguments) : NextAnyOrder(call, arguments);
            _used[index]++;
            _last = index;
            var step = Scenario.Steps[index];
            return (step.Status, step.Body?.ToJsonString());
        }
    }

    /// <summary>The body the fake answered the last call with, for the step's outputs that later steps expect.</summary>
    public void Answered(string? body)
    {
        lock (_gate)
        {
            if (_last < 0 || Scenario.Steps[_last] is not { Id: { } id, Outputs: { Count: > 0 } outputs })
            {
                return;
            }

            JsonNode? json;
            try
            {
                json = body is null ? null : JsonNode.Parse(body);
            }
            catch (System.Text.Json.JsonException)
            {
                json = null;
            }

            var values = new JsonObject();
            foreach (var (name, expression) in outputs)
            {
                values[name] = expression.StartsWith("$response.body", StringComparison.Ordinal)
                    ? Pointer(json, expression.IndexOf('#', StringComparison.Ordinal) is var hash and >= 0 ? expression[(hash + 1)..] : string.Empty)?.DeepClone()
                    : null;
            }

            _outputs[id] = values;
        }
    }

    public ScenarioReport Verify()
    {
        lock (_gate)
        {
            var failures = new List<string>(_failures);
            for (var i = 0; i < _used.Length; i++)
            {
                var step = Scenario.Steps[i];
                if (_used[i] < step.Times)
                {
                    failures.Add($"step {i + 1} ({step}) was called {_used[i]} of {step.Times} times");
                }
            }

            return new ScenarioReport(failures.Count == 0, failures, _calls.ToList());
        }
    }

    private int NextInOrder(FakeCall call, JsonObject arguments)
    {
        var current = FirstUnfinished();
        if (current < 0)
        {
            throw Fail($"{call.Operation} was called after the scenario's last step");
        }

        var step = Scenario.Steps[current];
        if (step.Operation == call.Operation)
        {
            return Mismatch(Resolve(step.Expect), arguments, "") is { } why
                ? throw Fail($"step {current + 1} ({step}): {why}")
                : current;
        }

        var later = Enumerable.Range(current + 1, Scenario.Steps.Count - current - 1)
            .FirstOrDefault(i => Scenario.Steps[i].Operation == call.Operation, -1);
        throw Fail(later >= 0
            ? $"{call.Operation} was called out of order: step {later + 1} ({Scenario.Steps[later]}) comes after step {current + 1} ({step})"
            : $"{call.Operation} was not expected; the next step is {current + 1} ({step})");
    }

    private int NextAnyOrder(FakeCall call, JsonObject arguments)
    {
        string? closest = null;
        for (var i = 0; i < Scenario.Steps.Count; i++)
        {
            var step = Scenario.Steps[i];
            if (step.Operation != call.Operation || _used[i] >= step.Times)
            {
                continue;
            }

            if (Mismatch(Resolve(step.Expect), arguments, "") is not { } why)
            {
                return i;
            }

            closest ??= $"step {i + 1} ({step}): {why}";
        }

        throw Fail(closest ?? $"{call.Operation} was not expected, or has no calls left");
    }

    /// <summary>
    /// The expectation with each <c>$steps.{id}.outputs.{name}</c> replaced by what that step answered; inside a string,
    /// <c>{$steps…}</c> is interpolated. One whose step hasn't answered becomes null, which matches anything.
    /// </summary>
    private JsonNode? Resolve(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                var copy = new JsonObject();
                foreach (var (key, value) in obj)
                {
                    copy[key] = Resolve(value);
                }

                return copy;
            case JsonArray array:
                return new JsonArray(array.Select(Resolve).ToArray());
            case JsonValue value when value.GetValueKind() == System.Text.Json.JsonValueKind.String:
                var text = value.GetValue<string>();
                if (text.StartsWith("$steps.", StringComparison.Ordinal))
                {
                    return Output(text)?.DeepClone();
                }

                if (!text.Contains("{$steps.", StringComparison.Ordinal))
                {
                    return value.DeepClone();
                }

                var unresolved = false;
                var interpolated = System.Text.RegularExpressions.Regex.Replace(text, @"\{(\$steps\.[^}]+)\}", m =>
                {
                    var output = Output(m.Groups[1].Value);
                    unresolved |= output is null;
                    return output is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.String ? v.GetValue<string>() : output?.ToJsonString() ?? string.Empty;
                });
                return unresolved ? null : JsonValue.Create(interpolated);
            default:
                return node?.DeepClone();
        }
    }

    private JsonNode? Output(string expression)
    {
        var parts = expression.Split('.');
        return parts.Length == 4 && parts[2] == "outputs" && _outputs.TryGetValue(parts[1], out var outputs) ? outputs[parts[3]] : null;
    }

    private static JsonNode? Pointer(JsonNode? node, string pointer)
    {
        foreach (var part in pointer.Split('/').Skip(1).Select(p => p.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal)))
        {
            node = node switch
            {
                JsonObject o => o[part],
                JsonArray a when int.TryParse(part, System.Globalization.CultureInfo.InvariantCulture, out var i) && i < a.Count => a[i],
                _ => null,
            };
        }

        return node;
    }

    private int FirstUnfinished()
    {
        for (var i = 0; i < _used.Length; i++)
        {
            if (_used[i] < Scenario.Steps[i].Times)
            {
                return i;
            }
        }

        return -1;
    }

    private FakeExpectationException Fail(string message)
    {
        _failures.Add(message);
        return new FakeExpectationException(message);
    }

    /// <summary>Null if <paramref name="actual"/> contains <paramref name="expected"/>; otherwise where and how it differs.</summary>
    internal static string? Mismatch(JsonNode? expected, JsonNode? actual, string path)
    {
        switch (expected)
        {
            case null:
                return null;
            case JsonObject expectedObject:
                if (actual is not JsonObject actualObject)
                {
                    return $"{Where(path)} expected an object, got {Show(actual)}";
                }

                foreach (var (key, value) in expectedObject)
                {
                    if (!actualObject.TryGetPropertyValue(key, out var actualValue))
                    {
                        return $"{Where(Join(path, key))} is missing";
                    }

                    if (Mismatch(value, actualValue, Join(path, key)) is { } why)
                    {
                        return why;
                    }
                }

                return null;
            default:
                return JsonNode.DeepEquals(expected, actual)
                    ? null
                    : $"{Where(path)} expected {expected.ToJsonString()}, got {Show(actual)}";
        }
    }

    private static string Join(string path, string key) => path.Length == 0 ? key : $"{path}.{key}";

    private static string Where(string path) => path.Length == 0 ? "the request" : path;

    private static string Show(JsonNode? node) => node?.ToJsonString() ?? "null";
}
