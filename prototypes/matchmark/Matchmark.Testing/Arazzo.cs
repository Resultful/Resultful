using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Matchmark.Testing;

/// <summary>
/// An Arazzo document (1.0 or 1.1, as JSON): workflows of calls against the spec's operations, with criteria that say
/// when each step succeeded, outputs passed between steps, and what to do on success or failure.
/// </summary>
public sealed class ArazzoDocument
{
    private ArazzoDocument(JsonObject document)
    {
        Document = document;
        Schemas = OpenApiSpec.Parse(document.ToJsonString());
        Workflows = (document["workflows"] as JsonArray ?? []).OfType<JsonObject>().Select(w => new ArazzoWorkflow(this, w)).ToList();
        OpenApiSources = (document["sourceDescriptions"] as JsonArray ?? []).OfType<JsonObject>()
            .Where(s => s["type"]?.GetValue<string>() is null or "openapi")
            .Select(s => s["name"]!.GetValue<string>())
            .ToList();
    }

    public JsonObject Document { get; }

    /// <summary>The document read as a schema context, so inputs' <c>$ref</c>s into its components resolve.</summary>
    internal OpenApiSpec Schemas { get; }

    public IReadOnlyList<ArazzoWorkflow> Workflows { get; }

    /// <summary>The names of the OpenAPI source descriptions, which prefix operationIds (<c>museum-api.getMuseumHours</c>).</summary>
    public IReadOnlyList<string> OpenApiSources { get; }

    public string Title => Document["info"]?["title"]?.GetValue<string>() ?? "Arazzo";

    public static ArazzoDocument Parse(string json)
        => new(JsonNode.Parse(json) as JsonObject ?? throw new ArgumentException("An Arazzo document is a JSON object.", nameof(json)));

    /// <summary>A <c>$components.parameters.x</c> (or similar) reference, resolved.</summary>
    internal JsonNode? Component(string reference)
    {
        var parts = reference.Split('.');
        return parts.Length == 3 && parts[0] == "$components" ? Document["components"]?[parts[1]]?[parts[2]] : null;
    }
}

public sealed class ArazzoWorkflow
{
    internal ArazzoWorkflow(ArazzoDocument document, JsonObject workflow)
    {
        Document = document;
        Json = workflow;
        Id = workflow["workflowId"]!.GetValue<string>();
        Steps = (workflow["steps"] as JsonArray ?? []).OfType<JsonObject>().ToList();
    }

    public ArazzoDocument Document { get; }

    public string Id { get; }

    internal JsonObject Json { get; }

    internal IReadOnlyList<JsonObject> Steps { get; }

    public string Name => $"{Document.Title}: {Id}";

    public override string ToString() => Name;
}

/// <summary>The state of a running workflow: its inputs, each step's outputs, and the exchange just made.</summary>
internal sealed class ArazzoContext(JsonObject inputs)
{
    public JsonObject Inputs { get; } = inputs;

    public Dictionary<string, JsonObject> Steps { get; } = new(StringComparer.Ordinal);

    public JsonObject Outputs { get; } = [];

    public Exchange? Current { get; set; }

    /// <summary>The last message a step sent or received: <c>{"payload": …, "headers": {…}}</c>.</summary>
    public JsonObject? Message { get; set; }

    /// <summary>
    /// A runtime expression: <c>$inputs.key</c>, <c>$steps.create.outputs.id</c>, <c>$outputs.x</c>,
    /// <c>$response.body#/id</c>, <c>$response.header.ETag</c>, <c>$request.path.id</c>, <c>$statusCode</c>, <c>$method</c>,
    /// <c>$url</c>. Anything else is a literal.
    /// </summary>
    public JsonNode? Evaluate(string expression)
    {
        var (head, pointer) = expression.IndexOf('#', StringComparison.Ordinal) is var hash and >= 0
            ? (expression[..hash], expression[(hash + 1)..])
            : (expression, null);
        var parts = head.Split('.');
        JsonNode? value = parts[0] switch
        {
            "$inputs" => Path(Inputs, parts.Skip(1)),
            "$outputs" => Path(Outputs, parts.Skip(1)),
            "$steps" when parts.Length >= 4 && parts[2] == "outputs" => Steps.TryGetValue(parts[1], out var step) ? Path(step, parts.Skip(3)) : null,
            "$message" when parts.Length >= 2 && parts[1] == "payload" => Message?["payload"]?.DeepClone(),
            "$message" when parts.Length >= 3 && parts[1] == "header" => Message?["headers"]?[string.Join('.', parts.Skip(2))]?.DeepClone(),
            "$statusCode" => Current is null ? null : JsonValue.Create(Current.Status),
            "$method" => Current is null ? null : JsonValue.Create(Current.Request.Method),
            "$url" => Current is null ? null : JsonValue.Create("/" + Current.Request.Target),
            "$response" when parts.Length >= 2 && parts[1] == "body" => Current?.Json()?.DeepClone(),
            "$response" when parts.Length >= 3 && parts[1] == "header" => Current is not null && Current.Headers.TryGetValue(string.Join('.', parts.Skip(2)), out var h) ? JsonValue.Create(h) : null,
            "$request" when parts.Length >= 2 && parts[1] == "body" => Current?.Request.Body is { } b ? Parse(b) : null,
            "$request" when parts.Length >= 3 && parts[1] == "header" => Current?.Request.Headers.FirstOrDefault(x => string.Equals(x.Key, parts[2], StringComparison.OrdinalIgnoreCase)).Value is { } v ? JsonValue.Create(v) : null,
            "$request" when parts.Length >= 3 && parts[1] == "path" => Current?.Request.PathParameters.GetValueOrDefault(parts[2]) is { } p ? JsonValue.Create(p) : null,
            "$request" when parts.Length >= 3 && parts[1] == "query" => Current?.Request.Query.FirstOrDefault(x => x.Key == parts[2]).Value is { } q ? JsonValue.Create(q) : null,
            _ => JsonValue.Create(expression),
        };
        return pointer is null ? value : Pointer(value, pointer);
    }

    /// <summary>Every runtime expression in a value replaced: a string that is one becomes its value; <c>{$…}</c> inside a string is interpolated.</summary>
    public JsonNode? Substitute(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                var copy = new JsonObject();
                foreach (var (key, value) in obj)
                {
                    copy[key] = Substitute(value);
                }

                return copy;
            case JsonArray array:
                return new JsonArray(array.Select(Substitute).ToArray());
            case JsonValue v when v.GetValueKind() == JsonValueKind.String:
                var text = v.GetValue<string>();
                if (text.StartsWith('$') && !text.Contains(' ', StringComparison.Ordinal))
                {
                    return Evaluate(text)?.DeepClone();
                }

                return text.Contains("{$", StringComparison.Ordinal)
                    ? JsonValue.Create(Regex.Replace(text, @"\{(\$[^}]+)\}", m => Text(Evaluate(m.Groups[1].Value))))
                    : v.DeepClone();
            default:
                return node?.DeepClone();
        }
    }

    public static string Text(JsonNode? value) => value?.GetValueKind() switch
    {
        null => string.Empty,
        JsonValueKind.String => value.GetValue<string>(),
        JsonValueKind.Number => SchemaValidator.NumberOf(value)?.ToString(CultureInfo.InvariantCulture) ?? value.ToJsonString(),
        _ => value.ToJsonString(),
    };

    private static JsonNode? Path(JsonNode? node, IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            node = node is JsonObject obj ? obj[name] : null;
        }

        return node?.DeepClone();
    }

    public static JsonNode? Pointer(JsonNode? node, string pointer)
    {
        foreach (var segment in pointer.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var name = segment.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            node = node is JsonArray array && int.TryParse(name, out var index) ? array.ElementAtOrDefault(index) : (node as JsonObject)?[name];
        }

        return node;
    }

    private static JsonNode? Parse(string json)
    {
        try
        {
            return JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return JsonValue.Create(json);
        }
    }
}

/// <summary>
/// Arazzo criteria: <c>simple</c> conditions (literals, runtime expressions, <c>== != &lt; &lt;= &gt; &gt;= ! &amp;&amp; || ()</c>,
/// and with a context, <c>$.path</c> into it), <c>regex</c> against the context, and <c>jsonpath</c> (RFC 9535: names,
/// indices, wildcards and <c>[?…]</c> filters), true when it selects something. XPath isn't supported.
/// </summary>
internal static class Criteria
{
    public static bool Supported(JsonObject criterion) => Type(criterion) is "simple" or "regex" or "jsonpath";

    public static string Type(JsonObject criterion) => criterion["type"] switch
    {
        JsonValue v => v.GetValue<string>(),
        JsonObject o => o["type"]?.GetValue<string>() ?? "simple",
        _ => "simple",
    };

    public static bool Holds(JsonObject criterion, ArazzoContext context)
    {
        var condition = criterion["condition"]?.GetValue<string>() ?? "true";
        var subject = criterion["context"]?.GetValue<string>() is { } c ? context.Evaluate(c) : null;
        return Type(criterion) switch
        {
            "regex" => Regex.IsMatch(ArazzoContext.Text(subject), condition, RegexOptions.None, TimeSpan.FromSeconds(1)),
            "jsonpath" => JsonPath(subject, condition).Count > 0,
            _ => Truthy(new Simple(condition, context, subject).Evaluate()),
        };
    }

    public static bool Truthy(JsonNode? value) => value?.GetValueKind() switch
    {
        null or JsonValueKind.Null or JsonValueKind.False => false,
        _ => true,
    };

    /// <summary>A JSONPath query (the RFC 9535 subset Arazzo criteria need), as the nodes it selects.</summary>
    public static List<JsonNode?> JsonPath(JsonNode? root, string query)
    {
        var nodes = new List<JsonNode?> { root };
        var i = query.StartsWith('$') ? 1 : 0;
        while (i < query.Length)
        {
            if (query[i] == '.')
            {
                var j = ++i;
                while (j < query.Length && query[j] is not ('.' or '['))
                {
                    j++;
                }

                var name = query[i..j];
                nodes = name == "*" ? nodes.SelectMany(Children).ToList() : nodes.Select(n => (n as JsonObject)?[name]).Where(n => n is not null).ToList();
                i = j;
            }
            else if (query[i] == '[')
            {
                var end = Close(query, i);
                var inside = query[(i + 1)..end].Trim();
                if (inside == "*")
                {
                    nodes = nodes.SelectMany(Children).ToList();
                }
                else if (inside.StartsWith('?'))
                {
                    var filter = inside[1..].Trim();
                    nodes = nodes.SelectMany(Children).Where(item => Truthy(new Simple(filter, null, item, current: item).Evaluate())).ToList();
                }
                else if (inside.StartsWith('\'') || inside.StartsWith('"'))
                {
                    var name = inside[1..^1];
                    nodes = nodes.Select(n => (n as JsonObject)?[name]).Where(n => n is not null).ToList();
                }
                else if (int.TryParse(inside, out var index))
                {
                    nodes = nodes.Select(n => n is JsonArray a ? a.ElementAtOrDefault(index < 0 ? a.Count + index : index) : null).Where(n => n is not null).ToList();
                }

                i = end + 1;
            }
            else
            {
                i++;
            }
        }

        return nodes;
    }

    private static IEnumerable<JsonNode?> Children(JsonNode? node) => node switch
    {
        JsonObject o => o.Select(p => p.Value),
        JsonArray a => a,
        _ => [],
    };

    private static int Close(string text, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            depth += text[i] == '[' ? 1 : text[i] == ']' ? -1 : 0;
            if (depth == 0)
            {
                return i;
            }
        }

        return text.Length - 1;
    }

    /// <summary>A simple condition, parsed and evaluated in one recursive descent.</summary>
    private sealed class Simple(string text, ArazzoContext? context, JsonNode? subject, JsonNode? current = null)
    {
        private int _at;

        public JsonNode? Evaluate()
        {
            var value = Or();
            Skip();
            return _at < text.Length ? throw new FormatException($"Unexpected '{text[_at..]}' in the condition '{text}'.") : value;
        }

        private JsonNode? Or()
        {
            var left = And();
            while (Take("||"))
            {
                var right = And();
                left = JsonValue.Create(Truthy(left) || Truthy(right));
            }

            return left;
        }

        private JsonNode? And()
        {
            var left = Unary();
            while (Take("&&"))
            {
                var right = Unary();
                left = JsonValue.Create(Truthy(left) && Truthy(right));
            }

            return left;
        }

        private JsonNode? Unary()
        {
            Skip();
            if (Peek("!") && !Peek("!="))
            {
                _at++;
                return JsonValue.Create(!Truthy(Unary()));
            }

            return Comparison();
        }

        private JsonNode? Comparison()
        {
            var left = Primary();
            foreach (var op in new[] { "==", "!=", "<=", ">=", "<", ">" })
            {
                if (Take(op))
                {
                    var right = Primary();
                    return JsonValue.Create(Compare(left, op, right));
                }
            }

            return left;
        }

        private JsonNode? Primary()
        {
            Skip();
            if (Take("("))
            {
                var inner = Or();
                return Take(")") ? inner : throw new FormatException($"Missing ')' in the condition '{text}'.");
            }

            if (_at < text.Length && text[_at] is '\'' or '"')
            {
                var quote = text[_at];
                var end = text.IndexOf(quote, _at + 1);
                var literal = text[(_at + 1)..end];
                _at = end + 1;
                return JsonValue.Create(literal);
            }

            var start = _at;
            while (_at < text.Length && !char.IsWhiteSpace(text[_at]) && !Operator())
            {
                _at += text[_at] == '[' ? Close(text, _at) - _at + 1 : 1;
            }

            var token = text[start.._at];
            return token switch
            {
                "" => throw new FormatException($"Expected a value at '{text[start..]}' in the condition '{text}'."),
                "true" => JsonValue.Create(true),
                "false" => JsonValue.Create(false),
                "null" => null,
                _ when decimal.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) => JsonValue.Create(n),
                _ when token.StartsWith('@') => JsonPath(current, "$" + token[1..]).FirstOrDefault(),
                _ when token.StartsWith("$.", StringComparison.Ordinal) || token.StartsWith("$[", StringComparison.Ordinal) => JsonPath(subject, token).FirstOrDefault(),
                _ when token.StartsWith('$') && context is not null => context.Evaluate(token),
                _ => JsonValue.Create(token),
            };
        }

        private bool Operator()
            => text[_at] is '(' or ')' or '=' or '<' or '>' || text[_at] == '!' && Peek("!=", _at) || Peek("&&", _at) || Peek("||", _at);

        private static bool Compare(JsonNode? left, string op, JsonNode? right)
        {
            if ((SchemaValidator.NumberOf(left) ?? Number(left)) is { } l && (SchemaValidator.NumberOf(right) ?? Number(right)) is { } r)
            {
                return op switch { "==" => l == r, "!=" => l != r, "<" => l < r, "<=" => l <= r, ">" => l > r, _ => l >= r };
            }

            var equal = JsonNode.DeepEquals(left, right) || ArazzoContext.Text(left) == ArazzoContext.Text(right) && left is not null && right is not null;
            var order = string.CompareOrdinal(ArazzoContext.Text(left), ArazzoContext.Text(right));
            return op switch { "==" => equal, "!=" => !equal, "<" => order < 0, "<=" => order <= 0, ">" => order > 0, _ => order >= 0 };
        }

        private static decimal? Number(JsonNode? node)
            => node?.GetValueKind() == JsonValueKind.String && decimal.TryParse(node.GetValue<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : null;

        private bool Take(string token)
        {
            Skip();
            if (!Peek(token, _at))
            {
                return false;
            }

            _at += token.Length;
            return true;
        }

        private bool Peek(string token) => Peek(token, _at);

        private bool Peek(string token, int at) => string.CompareOrdinal(text, at, token, 0, token.Length) == 0;

        private void Skip()
        {
            while (_at < text.Length && char.IsWhiteSpace(text[_at]))
            {
                _at++;
            }
        }
    }
}
