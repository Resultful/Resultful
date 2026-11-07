using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Resultful.OpenApi.Fakes;

/// <summary>Whether a scenario's steps must be called in the order written.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ScenarioOrder>))]
public enum ScenarioOrder
{
    /// <summary>Each call must match the next step. A step's <see cref="ScenarioStep.Times"/> calls come together.</summary>
    InOrder,

    /// <summary>Each call may match any step that still has calls left.</summary>
    AnyOrder,
}

/// <summary>
/// The calls a fake expects and the answers it gives, written in code (a generated typed builder) or declaratively
/// (JSON). Either way it is checked against the spec before the fake uses it.
/// </summary>
public sealed class Scenario
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public ScenarioOrder Order { get; init; } = ScenarioOrder.InOrder;

    public List<ScenarioStep> Steps { get; init; } = [];

    /// <summary>Reads a declarative scenario: <c>{"order": "InOrder", "steps": [{"operation": "getPet", ...}]}</c>.</summary>
    public static Scenario FromJson(string json)
        => JsonSerializer.Deserialize<Scenario>(json, Json) ?? throw new ArgumentException("The scenario is empty.", nameof(json));

    public string ToJson() => JsonSerializer.Serialize(this, Json);
}

/// <summary>One expected call: which operation, what the request must contain, and the documented response to give.</summary>
public sealed class ScenarioStep
{
    /// <summary>The operationId.</summary>
    public required string Operation { get; init; }

    /// <summary>
    /// What the request must contain: a subset of its arguments by parameter name, the body under <c>body</c>.
    /// Objects match if every property given matches; arrays and values must be equal. Null matches anything.
    /// </summary>
    public JsonObject? Expect { get; init; }

    /// <summary>The documented status to answer with.</summary>
    public required int Status { get; init; }

    /// <summary>The body to answer with; null for the spec's example or generated sample.</summary>
    public JsonNode? Body { get; init; }

    /// <summary>How many calls this step answers.</summary>
    public int Times { get; init; } = 1;

    public override string ToString() => $"{Operation} → {Status}" + (Expect is null ? string.Empty : $" expecting {Expect.ToJsonString()}");
}
