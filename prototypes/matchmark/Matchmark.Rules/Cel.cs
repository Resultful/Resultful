using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Matchmark.Rules;

/// <summary>
/// The subset of CEL (Common Expression Language) that schema rules need, as Kubernetes uses it in
/// <c>x-kubernetes-validations</c>: <c>self</c> and <c>oldSelf</c>; literals and lists; <c>! - * / % + - &lt; &lt;= &gt; &gt;= == !=
/// in &amp;&amp; || ?:</c>; member access and indexing; <c>has(x.f)</c>, <c>size()</c>; the macros <c>all</c>, <c>exists</c>,
/// <c>exists_one</c>, <c>map</c> and <c>filter</c>; <c>timestamp()</c>; Kubernetes' list functions <c>sum</c>, <c>min</c> and <c>max</c>; and the
/// string functions <c>contains</c>, <c>startsWith</c>, <c>endsWith</c> and <c>matches</c>. A missing field is an error, as in CEL.
/// </summary>
public static class Cel
{
    /// <summary>Evaluates a rule; an error (a missing field, a type that doesn't fit) is reported, not thrown.</summary>
    public static (bool Holds, string? Error) Check(string rule, JsonNode? self, JsonNode? oldSelf = null, bool hasOld = false)
    {
        try
        {
            var variables = new Dictionary<string, JsonNode?>(StringComparer.Ordinal) { ["self"] = self };
            if (hasOld)
            {
                variables["oldSelf"] = oldSelf;
            }

            return (new Parser(rule, variables).Evaluate() is JsonValue v && v.GetValueKind() == JsonValueKind.True, null);
        }
        catch (CelException e)
        {
            return (false, e.Message);
        }
    }

    /// <summary>
    /// Evaluates an expression to a value, over the variables given (<c>request</c>, <c>self</c>, <c>oldSelf</c> for a
    /// derived field); an error is reported, not thrown.
    /// </summary>
    public static (JsonNode? Value, string? Error) Evaluate(string expression, IReadOnlyDictionary<string, JsonNode?> variables)
    {
        try
        {
            return (new Parser(expression, new Dictionary<string, JsonNode?>(variables, StringComparer.Ordinal)).Evaluate(), null);
        }
        catch (CelException e)
        {
            return (null, e.Message);
        }
    }

    public sealed class CelException(string message) : Exception(message);

    private sealed class Parser(string text, Dictionary<string, JsonNode?> variables)
    {
        private int _at;

        public JsonNode? Evaluate()
        {
            var value = Ternary();
            Skip();
            return _at < text.Length ? throw new CelException($"unexpected '{text[_at..]}'") : value;
        }

        private JsonNode? Ternary()
        {
            var condition = Or();
            if (!Take("?"))
            {
                return condition;
            }

            var yes = Ternary();
            Expect(":");
            var no = Ternary();
            return Bool(condition) ? yes : no;
        }

        private JsonNode? Or()
        {
            var left = And();
            while (Take("||"))
            {
                var right = And();
                left = JsonValue.Create(Bool(left) || Bool(right));
            }

            return left;
        }

        private JsonNode? And()
        {
            var left = Relation();
            while (Take("&&"))
            {
                var right = Relation();
                left = JsonValue.Create(Bool(left) && Bool(right));
            }

            return left;
        }

        private JsonNode? Relation()
        {
            var left = Additive();
            foreach (var op in new[] { "==", "!=", "<=", ">=", "<", ">" })
            {
                if (Take(op))
                {
                    return JsonValue.Create(Compare(left, op, Additive()));
                }
            }

            if (TakeWord("in"))
            {
                var list = Additive() as JsonArray ?? throw new CelException("'in' needs a list");
                return JsonValue.Create(list.Any(item => JsonEquals(item, left)));
            }

            return left;
        }

        private JsonNode? Additive()
        {
            var left = Multiplicative();
            while (true)
            {
                if (Take("+"))
                {
                    var right = Multiplicative();
                    left = left is JsonValue l && l.GetValueKind() == JsonValueKind.String ? JsonValue.Create(l.GetValue<string>() + Text(right))
                        : left is JsonArray la && right is JsonArray ra ? new JsonArray(la.Concat(ra).Select(n => n?.DeepClone()).ToArray())
                        : JsonValue.Create(Number(left) + Number(right));
                }
                else if (Take("-"))
                {
                    left = JsonValue.Create(Number(left) - Number(Multiplicative()));
                }
                else
                {
                    return left;
                }
            }
        }

        private JsonNode? Multiplicative()
        {
            var left = Unary();
            while (true)
            {
                if (Take("*"))
                {
                    left = JsonValue.Create(Number(left) * Number(Unary()));
                }
                else if (Take("/"))
                {
                    var divisor = Number(Unary());
                    left = divisor == 0 ? throw new CelException("division by zero") : JsonValue.Create(Number(left) / divisor);
                }
                else if (Take("%"))
                {
                    left = JsonValue.Create(Number(left) % Number(Unary()));
                }
                else
                {
                    return left;
                }
            }
        }

        private JsonNode? Unary()
        {
            Skip();
            if (Peek("!") && !Peek("!="))
            {
                _at++;
                return JsonValue.Create(!Bool(Unary()));
            }

            if (Peek("-") && !char.IsDigit(text.ElementAtOrDefault(_at + 1)))
            {
                _at++;
                return JsonValue.Create(-Number(Unary()));
            }

            return Member(Primary());
        }

        /// <summary>Field access, indexing and method calls (including macros) on a value.</summary>
        private JsonNode? Member(JsonNode? value)
        {
            while (true)
            {
                if (Take("."))
                {
                    var name = Identifier();
                    if (Take("("))
                    {
                        value = Method(value, name);
                    }
                    else
                    {
                        value = value is JsonObject obj && obj.TryGetPropertyValue(name, out var field)
                            ? field
                            : throw new CelException($"no such field '{name}'");
                    }
                }
                else if (Take("["))
                {
                    var index = Ternary();
                    Expect("]");
                    value = value switch
                    {
                        JsonArray array when Number(index) is var i && i >= 0 && i < array.Count => array[(int)i],
                        JsonObject obj when Text(index) is var key && obj.TryGetPropertyValue(key, out var field) => field,
                        _ => throw new CelException($"no element {Text(index)}"),
                    };
                }
                else
                {
                    return value;
                }
            }
        }

        private JsonNode? Method(JsonNode? target, string name)
        {
            if (name is "all" or "exists" or "exists_one" or "map" or "filter")
            {
                var variable = Identifier();
                Expect(",");
                var start = _at;
                var items = target as JsonArray ?? throw new CelException($"'{name}' needs a list");
                var results = new List<JsonNode?>();
                var end = start;
                foreach (var item in items)
                {
                    _at = start;
                    variables[variable] = item;
                    results.Add(Ternary());
                    end = _at;
                }

                if (items.Count == 0)
                {
                    SkipArgument();
                    end = _at;
                }

                variables.Remove(variable);
                _at = end;
                Expect(")");
                return name switch
                {
                    "all" => JsonValue.Create(results.All(Bool)),
                    "exists" => JsonValue.Create(results.Any(Bool)),
                    "exists_one" => JsonValue.Create(results.Count(Bool) == 1),
                    "map" => new JsonArray(results.Select(r => r?.DeepClone()).ToArray()),
                    _ => new JsonArray(items.Where((_, i) => Bool(results[i])).Select(n => n?.DeepClone()).ToArray()),
                };
            }

            var args = new List<JsonNode?>();
            if (!Take(")"))
            {
                do
                {
                    args.Add(Ternary());
                }
                while (Take(","));
                Expect(")");
            }

            return name switch
            {
                "size" => JsonValue.Create(Size(target)),
                "sum" => JsonValue.Create((target as JsonArray ?? throw new CelException("'sum' needs a list")).Sum(Number)),
                "min" => JsonValue.Create((target as JsonArray ?? throw new CelException("'min' needs a list")).Min(Number)),
                "max" => JsonValue.Create((target as JsonArray ?? throw new CelException("'max' needs a list")).Max(Number)),
                "contains" => JsonValue.Create(Text(target).Contains(Text(args[0]), StringComparison.Ordinal)),
                "startsWith" => JsonValue.Create(Text(target).StartsWith(Text(args[0]), StringComparison.Ordinal)),
                "endsWith" => JsonValue.Create(Text(target).EndsWith(Text(args[0]), StringComparison.Ordinal)),
                "matches" => JsonValue.Create(Regex.IsMatch(Text(target), Text(args[0]), RegexOptions.None, TimeSpan.FromSeconds(1))),
                _ => throw new CelException($"no function '{name}'"),
            };
        }

        private JsonNode? Primary()
        {
            Skip();
            if (Take("("))
            {
                var inner = Ternary();
                Expect(")");
                return inner;
            }

            if (Take("["))
            {
                var list = new JsonArray();
                if (!Take("]"))
                {
                    do
                    {
                        list.Add(Ternary()?.DeepClone());
                    }
                    while (Take(","));
                    Expect("]");
                }

                return list;
            }

            if (_at < text.Length && text[_at] is '\'' or '"')
            {
                var quote = text[_at];
                var end = text.IndexOf(quote, _at + 1);
                var literal = text[(_at + 1)..end];
                _at = end + 1;
                return JsonValue.Create(literal);
            }

            if (_at < text.Length && (char.IsDigit(text[_at]) || text[_at] == '-'))
            {
                var start = _at++;
                while (_at < text.Length && (char.IsDigit(text[_at]) || text[_at] is '.' or 'e' or 'E'))
                {
                    _at++;
                }

                return JsonValue.Create(decimal.Parse(text[start.._at], NumberStyles.Float, CultureInfo.InvariantCulture));
            }

            var name = Identifier();
            switch (name)
            {
                case "true":
                    return JsonValue.Create(true);
                case "false":
                    return JsonValue.Create(false);
                case "null":
                    return null;
                case "has":
                    Expect("(");
                    var present = Has();
                    Expect(")");
                    return JsonValue.Create(present);
                case "size":
                    Expect("(");
                    var sized = Ternary();
                    Expect(")");
                    return JsonValue.Create(Size(sized));
                case "timestamp":
                    // An RFC 3339 string as an instant, written in UTC so that instants compare in order.
                    Expect("(");
                    var instant = Ternary();
                    Expect(")");
                    return DateTimeOffset.TryParse(Text(instant), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
                        ? JsonValue.Create(at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture))
                        : throw new CelException($"{Text(instant)} isn't a timestamp");
            }

            return variables.TryGetValue(name, out var value) ? value : throw new CelException($"no variable '{name}'");
        }

        /// <summary>has(a.b.c): whether the last field is present, the rest evaluated as usual.</summary>
        private bool Has()
        {
            var start = _at;
            var name = Identifier();
            if (!variables.TryGetValue(name, out var value))
            {
                return false;
            }

            var field = (string?)null;
            while (Take("."))
            {
                if (field is not null)
                {
                    value = value is JsonObject o && o.TryGetPropertyValue(field, out var next) ? next : null;
                }

                field = Identifier();
            }

            return field is null ? value is not null : value is JsonObject obj && obj.ContainsKey(field);
        }

        private void SkipArgument()
        {
            var depth = 0;
            while (_at < text.Length && !(depth == 0 && text[_at] == ')'))
            {
                depth += text[_at] is '(' or '[' ? 1 : text[_at] is ')' or ']' ? -1 : 0;
                _at++;
            }
        }

        private static long Size(JsonNode? value) => value switch
        {
            JsonArray a => a.Count,
            JsonObject o => o.Count,
            JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>().Length,
            _ => throw new CelException("'size' needs a list, map or string"),
        };

        private static bool Compare(JsonNode? left, string op, JsonNode? right)
        {
            if (op is "==" or "!=")
            {
                return JsonEquals(left, right) == (op == "==");
            }

            var order = NumberOf(left) is { } l && NumberOf(right) is { } r ? l.CompareTo(r) : string.CompareOrdinal(Text(left), Text(right));
            return op switch { "<" => order < 0, "<=" => order <= 0, ">" => order > 0, _ => order >= 0 };
        }

        private static bool JsonEquals(JsonNode? a, JsonNode? b)
            => NumberOf(a) is { } x && NumberOf(b) is { } y ? x == y : JsonNode.DeepEquals(a, b);

        private static bool Bool(JsonNode? value)
            => value is JsonValue v && v.GetValueKind() is JsonValueKind.True or JsonValueKind.False ? v.GetValueKind() == JsonValueKind.True : throw new CelException("expected true or false");

        private static decimal Number(JsonNode? value) => NumberOf(value) ?? throw new CelException($"expected a number, not {value?.ToJsonString() ?? "null"}");

        private static decimal? NumberOf(JsonNode? node)
            => node is JsonValue value && value.GetValueKind() == JsonValueKind.Number
               && decimal.TryParse(value.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                ? number
                : null;

        private static string Text(JsonNode? value) => value is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : value?.ToJsonString() ?? "null";

        private string Identifier()
        {
            Skip();
            var start = _at;
            while (_at < text.Length && (char.IsLetterOrDigit(text[_at]) || text[_at] == '_'))
            {
                _at++;
            }

            return _at > start ? text[start.._at] : throw new CelException($"expected a name at '{text[start..]}'");
        }

        private void Expect(string token)
        {
            if (!Take(token))
            {
                throw new CelException($"expected '{token}' at '{text[_at..]}'");
            }
        }

        private bool Take(string token)
        {
            Skip();
            if (!Peek(token))
            {
                return false;
            }

            _at += token.Length;
            return true;
        }

        private bool TakeWord(string word)
        {
            Skip();
            if (!Peek(word) || _at + word.Length < text.Length && char.IsLetterOrDigit(text[_at + word.Length]))
            {
                return false;
            }

            _at += word.Length;
            return true;
        }

        private bool Peek(string token) => string.CompareOrdinal(text, _at, token, 0, token.Length) == 0;

        private void Skip()
        {
            while (_at < text.Length && char.IsWhiteSpace(text[_at]))
            {
                _at++;
            }
        }
    }
}
