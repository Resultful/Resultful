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
            var step = Scenario.Steps[index];
            return (step.Status, step.Body?.ToJsonString());
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
            return Mismatch(step.Expect, arguments, "") is { } why
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

            if (Mismatch(step.Expect, arguments, "") is not { } why)
            {
                return i;
            }

            closest ??= $"step {i + 1} ({step}): {why}";
        }

        throw Fail(closest ?? $"{call.Operation} was not expected, or has no calls left");
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
