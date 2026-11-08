using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Matchmark.Generator;

namespace Matchmark.Cli;

/// <summary>How a change to a spec affects those who already use it.</summary>
public enum ChangeKind
{
    /// <summary>An existing client or server stops working: a request it sends is refused, or a response it reads is new to it.</summary>
    Breaking,

    /// <summary>On the wire nothing breaks, but code generated from the old spec no longer compiles or matches (a renamed operation).</summary>
    Generated,

    /// <summary>Nothing that worked stops working.</summary>
    Additive,
}

public sealed record SpecChange(ChangeKind Kind, string Subject, string Message)
{
    public override string ToString() => $"{Kind.ToString().ToLowerInvariant(),-9} {Subject}: {Message}";
}

/// <summary>
/// <c>diff &lt;old&gt; &lt;new&gt;</c>: what changed between two versions of a spec, and whom it breaks. A request schema may
/// only loosen and a response schema only tighten; anything else breaks a client written against the old one. The exit
/// code fails a breaking change unless the major version went up with it.
/// </summary>
public static class SpecDiff
{
    public const string Usage = """
        usage: matchmark diff <old spec> <new spec> [--allow-breaking]

          Lists what changed and whom it breaks: breaking (an existing client or server stops working), generated (code
          generated from the old spec stops compiling, as a renamed operation does) or additive. Exits 1 when a change
          is breaking and info.version's major number didn't go up, unless --allow-breaking.
        """;

    private static readonly string[] Methods = ["get", "put", "post", "delete", "options", "head", "patch", "trace", "query"];

    public static async Task<int> RunAsync(string[] args, TextWriter output, CancellationToken cancellationToken = default)
    {
        if (args.Length < 2)
        {
            await output.WriteLineAsync(Usage);
            return 2;
        }

        string before, after;
        try
        {
            before = SpecJson.Normalize(await File.ReadAllTextAsync(args[0], cancellationToken), args[0]);
            after = SpecJson.Normalize(await File.ReadAllTextAsync(args[1], cancellationToken), args[1]);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or SharpYaml.YamlException or JsonException)
        {
            await output.WriteLineAsync($"error: {e.Message}");
            return 2;
        }

        var changes = Compare(before, after);
        foreach (var change in changes.OrderBy(c => c.Kind))
        {
            await output.WriteLineAsync(change.ToString());
        }

        var (verdict, failed) = Verdict(changes, Version(before), Version(after), args.Contains("--allow-breaking"));
        await output.WriteLineAsync(verdict);
        return failed ? 1 : 0;
    }

    /// <summary>Whether the changes may ship under the new version: a breaking change needs a new major version.</summary>
    public static (string Verdict, bool Failed) Verdict(IReadOnlyList<SpecChange> changes, string? before, string? after, bool allowBreaking)
    {
        var breaking = changes.Count(c => c.Kind == ChangeKind.Breaking);
        var generated = changes.Count(c => c.Kind == ChangeKind.Generated);
        var summary = $"{changes.Count} changes: {breaking} breaking, {generated} breaking generated code, {changes.Count - breaking - generated} additive.";
        if (breaking == 0)
        {
            return ($"{summary} Compatible.", false);
        }

        if (Major(after) is { } newMajor && Major(before) is { } oldMajor && newMajor > oldMajor)
        {
            return ($"{summary} Breaking, and versioned: {before} to {after}.", false);
        }

        return allowBreaking
            ? ($"{summary} Breaking, allowed by --allow-breaking.", false)
            : ($"{summary} Breaking, but the major version didn't go up ({before ?? "none"} to {after ?? "none"}).", true);
    }

    private static string? Version(string spec) => JsonNode.Parse(spec)?["info"]?["version"]?.GetValue<string>();

    private static int? Major(string? version)
        => version is not null && int.TryParse(version.TrimStart('v', 'V').Split('.', '-')[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major) ? major : null;

    /// <summary>Every change from one normalized spec (JSON) to another.</summary>
    public static IReadOnlyList<SpecChange> Compare(string beforeJson, string afterJson)
    {
        var before = JsonNode.Parse(beforeJson)!.AsObject();
        var after = JsonNode.Parse(afterJson)!.AsObject();
        var diff = new Differ(before, after);
        var old = Operations(before);
        var current = Operations(after);
        foreach (var (key, operation) in old)
        {
            if (!current.TryGetValue(key, out var now))
            {
                diff.Add(ChangeKind.Breaking, key, "the operation was removed");
                continue;
            }

            diff.Operation(Name(key, operation), operation, now);
        }

        foreach (var key in current.Keys.Where(k => !old.ContainsKey(k)))
        {
            diff.Add(ChangeKind.Additive, Name(key, current[key]), "the operation was added");
        }

        return diff.Changes;
    }

    private static string Name(string key, JsonObject operation) => operation["operationId"]?.GetValue<string>() ?? key;

    /// <summary>Operations by method and path, path parameters unnamed (renaming {petId} to {id} is no change on the wire).</summary>
    private static Dictionary<string, JsonObject> Operations(JsonObject spec)
    {
        var operations = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var (path, item) in spec["paths"] as JsonObject ?? [])
        {
            var shape = System.Text.RegularExpressions.Regex.Replace(path, @"\{[^}]+\}", "{}");
            foreach (var method in Methods)
            {
                if (item?[method] is JsonObject operation)
                {
                    // Path-level parameters belong to each operation.
                    var copy = (JsonObject)operation.DeepClone();
                    if (item["parameters"] is JsonArray shared)
                    {
                        var own = copy["parameters"] as JsonArray ?? [];
                        copy["parameters"] = new JsonArray(own.Select(p => p?.DeepClone()).Concat(shared.Select(p => p?.DeepClone())).ToArray());
                    }

                    operations[$"{method.ToUpperInvariant()} {shape}"] = copy;
                }
            }
        }

        return operations;
    }

    private enum Direction
    {
        Request,
        Response,
    }

    private sealed class Differ(JsonObject before, JsonObject after)
    {
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

        public List<SpecChange> Changes { get; } = [];

        public void Add(ChangeKind kind, string subject, string message) => Changes.Add(new SpecChange(kind, subject, message));

        public void Operation(string name, JsonObject old, JsonObject now)
        {
            if (old["operationId"]?.GetValue<string>() is { } oldId && now["operationId"]?.GetValue<string>() is { } newId && oldId != newId)
            {
                Add(ChangeKind.Generated, name, $"the operationId changed from {oldId} to {newId}");
            }

            Parameters(name, old, now);
            Body(name, Resolve(before, old["requestBody"]), Resolve(after, now["requestBody"]));
            Responses(name, old["responses"] as JsonObject ?? [], now["responses"] as JsonObject ?? []);
        }

        private void Parameters(string name, JsonObject old, JsonObject now)
        {
            static Dictionary<string, JsonObject> Index(JsonObject spec, JsonObject operation)
                => (operation["parameters"] as JsonArray ?? []).Select(p => Resolve(spec, p)).OfType<JsonObject>()
                    .GroupBy(p => $"{p["in"]}.{p["name"]}").ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            var was = Index(before, old);
            var @is = Index(after, now);
            foreach (var (key, parameter) in was)
            {
                if (!@is.TryGetValue(key, out var current))
                {
                    if (!key.StartsWith("path.", StringComparison.Ordinal))
                    {
                        Add(ChangeKind.Generated, name, $"the parameter {key} was removed");
                    }

                    continue;
                }

                if (!Required(parameter) && Required(current))
                {
                    Add(ChangeKind.Breaking, name, $"the parameter {key} is now required");
                }
                else if (Required(parameter) && !Required(current))
                {
                    Add(ChangeKind.Additive, name, $"the parameter {key} is now optional");
                }

                Schema(name, $"parameter {key}", parameter["schema"], current["schema"], Direction.Request);
            }

            foreach (var (key, parameter) in @is.Where(p => !was.ContainsKey(p.Key)))
            {
                Add(Required(parameter) ? ChangeKind.Breaking : ChangeKind.Additive, name,
                    $"the {(Required(parameter) ? "required" : "optional")} parameter {key} was added");
            }
        }

        private void Body(string name, JsonObject? old, JsonObject? now)
        {
            if (old is null && now is null)
            {
                return;
            }

            if (old is null)
            {
                Add(Required(now!) ? ChangeKind.Breaking : ChangeKind.Additive, name, $"a {(Required(now!) ? "required" : "optional")} request body was added");
                return;
            }

            if (now is null)
            {
                Add(ChangeKind.Generated, name, "the request body was removed");
                return;
            }

            if (!Required(old) && Required(now))
            {
                Add(ChangeKind.Breaking, name, "the request body is now required");
            }

            foreach (var (media, content) in old["content"] as JsonObject ?? [])
            {
                if (now["content"]?[media] is not { } current)
                {
                    Add(ChangeKind.Breaking, name, $"the request body can no longer be {media}");
                    continue;
                }

                Schema(name, "request body", content?["schema"], current["schema"], Direction.Request);
            }

            foreach (var (media, _) in (now["content"] as JsonObject ?? []).Where(c => old["content"]?[c.Key] is null))
            {
                Add(ChangeKind.Additive, name, $"the request body can also be {media}");
            }
        }

        private void Responses(string name, JsonObject old, JsonObject now)
        {
            foreach (var (status, response) in old)
            {
                if (now[status] is not { } current)
                {
                    // A client can't receive what's gone; generated code loses a case of its union.
                    Add(status.StartsWith('2') ? ChangeKind.Breaking : ChangeKind.Generated, name, $"the {status} response was removed");
                    continue;
                }

                var was = Resolve(before, response) ?? [];
                var @is = Resolve(after, current) ?? [];
                foreach (var (media, content) in was["content"] as JsonObject ?? [])
                {
                    if (@is["content"]?[media] is not { } now2)
                    {
                        Add(ChangeKind.Breaking, name, $"the {status} response is no longer {media}");
                        continue;
                    }

                    Schema(name, $"{status} response", content?["schema"] ?? content?["itemSchema"], now2["schema"] ?? now2["itemSchema"], Direction.Response);
                }

                foreach (var (media, _) in (@is["content"] as JsonObject ?? []).Where(c => was["content"]?[c.Key] is null))
                {
                    Add(ChangeKind.Breaking, name, $"the {status} response can now be {media}, which a client of the old spec doesn't expect");
                }

                foreach (var (header, value) in was["headers"] as JsonObject ?? [])
                {
                    if (Required(Resolve(before, value) ?? []) && @is["headers"]?[header] is null)
                    {
                        Add(ChangeKind.Breaking, name, $"the {status} response no longer has the required header {header}");
                    }
                }
            }

            foreach (var (status, _) in now.Where(r => old[r.Key] is null))
            {
                // A client of the old spec sees an undocumented status (the generated one refuses it).
                Add(ChangeKind.Breaking, name, $"the {status} response was added, which a client of the old spec doesn't expect");
            }
        }

        /// <summary>
        /// A schema, old against new. A request may loosen (accept more) and a response may tighten (send less); the
        /// opposite breaks someone written against the old one.
        /// </summary>
        private void Schema(string name, string where, JsonNode? oldNode, JsonNode? newNode, Direction direction)
        {
            var old = Resolve(before, oldNode);
            var now = Resolve(after, newNode);
            if (old is null || now is null || where.Count(c => c is '.' or '[') > 24
                || !_seen.Add($"{direction}|{RefOf(oldNode)}|{RefOf(newNode)}|{where}") && RefOf(oldNode) is not null)
            {
                return;
            }

            var request = direction == Direction.Request;

            // Types: a request may accept more types, a response may send fewer.
            var oldTypes = Types(old);
            var newTypes = Types(now);
            if (oldTypes.Count > 0 && newTypes.Count > 0 && !oldTypes.SetEquals(newTypes))
            {
                var narrowed = oldTypes.Except(newTypes).ToList();
                var widened = newTypes.Except(oldTypes).ToList();
                var breaks = request ? narrowed.Count > 0 : widened.Count > 0;
                Add(breaks ? ChangeKind.Breaking : ChangeKind.Additive, name,
                    $"{where} is now {string.Join(" or ", newTypes.Order())}, was {string.Join(" or ", oldTypes.Order())}");
            }

            Bounds(name, where, old, now, request);
            Enums(name, where, old, now, request);
            Properties(name, where, old, now, direction);

            if (old["items"] is { } oldItems && now["items"] is { } newItems)
            {
                Schema(name, $"{where}[]", oldItems, newItems, direction);
            }

            foreach (var keyword in new[] { "oneOf", "anyOf" })
            {
                var oldMembers = (old[keyword] as JsonArray ?? []).Select(RefOf).Where(r => r is not null).ToHashSet();
                var newMembers = (now[keyword] as JsonArray ?? []).Select(RefOf).Where(r => r is not null).ToHashSet();
                foreach (var added in newMembers.Except(oldMembers))
                {
                    Add(request ? ChangeKind.Additive : ChangeKind.Breaking, name, $"{where} can now be {Short(added!)}");
                }

                foreach (var removed in oldMembers.Except(newMembers))
                {
                    Add(request ? ChangeKind.Breaking : ChangeKind.Generated, name, $"{where} can no longer be {Short(removed!)}");
                }
            }
        }

        private void Bounds(string name, string where, JsonObject old, JsonObject now, bool request)
        {
            // Each keyword, and whether a bigger value accepts more.
            foreach (var (keyword, looser) in new[] { ("maxLength", true), ("maximum", true), ("maxItems", true), ("minLength", false), ("minimum", false), ("minItems", false) })
            {
                var was = Number(old[keyword]);
                var @is = Number(now[keyword]);
                if (was == @is)
                {
                    continue;
                }

                // Absent is the loosest bound of all.
                var loosened = @is is null || was is not null && (looser ? @is > was : @is < was);
                var change = $"{where}'s {keyword} {(@is is null ? $"was removed (was {Show(was)})" : was is null ? $"is now {Show(@is)}" : $"went from {Show(was)} to {Show(@is)}")}";
                Add(loosened == request ? ChangeKind.Additive : request ? ChangeKind.Breaking : ChangeKind.Generated, name, change);
            }

            var oldPattern = old["pattern"]?.GetValue<string>();
            var newPattern = now["pattern"]?.GetValue<string>();
            if (oldPattern != newPattern)
            {
                Add(request && newPattern is not null ? ChangeKind.Breaking : ChangeKind.Additive, name,
                    newPattern is null ? $"{where}'s pattern was removed" : $"{where}'s pattern is now {newPattern}{(oldPattern is null ? string.Empty : $", was {oldPattern}")}");
            }
        }

        private void Enums(string name, string where, JsonObject old, JsonObject now, bool request)
        {
            if (old["enum"] is not JsonArray oldValues && now["enum"] is not JsonArray)
            {
                return;
            }

            var was = (old["enum"] as JsonArray ?? []).Select(v => v?.ToJsonString() ?? "null").ToHashSet(StringComparer.Ordinal);
            var @is = (now["enum"] as JsonArray ?? []).Select(v => v?.ToJsonString() ?? "null").ToHashSet(StringComparer.Ordinal);
            foreach (var removed in was.Except(@is).Where(_ => @is.Count > 0))
            {
                Add(request ? ChangeKind.Breaking : ChangeKind.Generated, name, $"{where} can no longer be {removed}");
            }

            foreach (var added in @is.Except(was).Where(_ => was.Count > 0))
            {
                // A generated enum refuses a value it doesn't know.
                Add(request ? ChangeKind.Additive : ChangeKind.Breaking, name, $"{where} can now be {added}");
            }
        }

        private void Properties(string name, string where, JsonObject old, JsonObject now, Direction direction)
        {
            var request = direction == Direction.Request;
            var oldProperties = old["properties"] as JsonObject ?? [];
            var newProperties = now["properties"] as JsonObject ?? [];
            var oldRequired = Strings(old["required"]);
            var newRequired = Strings(now["required"]);
            foreach (var (property, schema) in oldProperties)
            {
                var at = $"{where}.{property}";
                if (newProperties[property] is not { } current)
                {
                    var closed = now["additionalProperties"]?.GetValueKind() == JsonValueKind.False;
                    Add(request ? (closed ? ChangeKind.Breaking : ChangeKind.Generated) : oldRequired.Contains(property) ? ChangeKind.Breaking : ChangeKind.Generated,
                        name, $"{at} was removed");
                    continue;
                }

                var wasRequired = oldRequired.Contains(property);
                var isRequired = newRequired.Contains(property);
                if (wasRequired != isRequired)
                {
                    // A request that must now send it breaks; a response that may now leave it out breaks.
                    var breaks = request ? isRequired : wasRequired;
                    Add(breaks ? ChangeKind.Breaking : ChangeKind.Additive, name, $"{at} is now {(isRequired ? "required" : "optional")}");
                }

                Schema(name, at, schema, current, direction);
            }

            foreach (var (property, _) in newProperties.Where(p => oldProperties[p.Key] is null))
            {
                var required = newRequired.Contains(property);
                Add(request && required ? ChangeKind.Breaking : ChangeKind.Additive, name,
                    $"{where}.{property} was added{(required ? ", required" : string.Empty)}");
            }
        }

        private static HashSet<string> Types(JsonObject schema)
        {
            var types = schema["type"] switch
            {
                JsonValue v when v.GetValueKind() == JsonValueKind.String => [v.GetValue<string>()],
                JsonArray a => a.Select(t => t!.GetValue<string>()).ToHashSet(StringComparer.Ordinal),
                _ => new HashSet<string>(StringComparer.Ordinal),
            };
            if (schema["nullable"]?.GetValueKind() == JsonValueKind.True)
            {
                types.Add("null");
            }

            // An integer is a number: going from integer to number accepts more.
            return types;
        }

        private static decimal? Number(JsonNode? node)
            => node is JsonValue v && v.GetValueKind() == JsonValueKind.Number && decimal.TryParse(v.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;

        private static string Show(decimal? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "none";

        private static HashSet<string> Strings(JsonNode? node)
            => (node as JsonArray ?? []).Select(n => n?.GetValue<string>()).Where(n => n is not null).Select(n => n!).ToHashSet(StringComparer.Ordinal);

        private static string Short(string reference) => reference[(reference.LastIndexOf('/') + 1)..];
    }

    private static bool Required(JsonObject node) => node["required"]?.GetValueKind() == JsonValueKind.True;

    private static string? RefOf(JsonNode? node) => node?["$ref"]?.GetValue<string>();

    private static JsonObject? Resolve(JsonObject spec, JsonNode? node)
    {
        for (var i = 0; i < 32 && node?["$ref"]?.GetValue<string>() is { } reference; i++)
        {
            node = reference.StartsWith('#') ? Audit.At(spec, reference[1..]) : null;
        }

        return node as JsonObject;
    }

    /// <summary>The changes as text, one per line, breaking first.</summary>
    public static string Text(IReadOnlyList<SpecChange> changes)
    {
        var text = new StringBuilder();
        foreach (var change in changes.OrderBy(c => c.Kind))
        {
            text.AppendLine(change.ToString());
        }

        return text.ToString();
    }
}
