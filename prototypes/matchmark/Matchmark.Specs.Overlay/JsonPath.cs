using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Matchmark.Specs.Overlay
{
    /// <summary>
    /// The RFC 9535 JSONPath that Overlay targets use: names (<c>.a</c>, <c>['a']</c>), indices, wildcards, descendants
    /// (<c>..a</c>), and filters (<c>[?@.a == 'b']</c>, <c>[?(@.a)]</c>, nested queries as in the traits example).
    /// </summary>
    public static class JsonPath
    {
        public static List<JsonNode> Select(JsonNode root, string query)
        {
            var nodes = new List<JsonNode?> { root };
            var at = query.StartsWith("$", StringComparison.Ordinal) ? 1 : 0;
            while (at < query.Length)
            {
                if (query[at] == '.' && at + 1 < query.Length && query[at + 1] == '.')
                {
                    at += 2;
                    var (segment, next) = Segment(query, at);
                    nodes = nodes.SelectMany(Descendants).ToList();
                    nodes = Step(nodes, segment);
                    at = next;
                }
                else if (query[at] == '.')
                {
                    var (segment, next) = Segment(query, at + 1);
                    nodes = Step(nodes, segment);
                    at = next;
                }
                else if (query[at] == '[')
                {
                    var end = Close(query, at);
                    nodes = Step(nodes, query.Substring(at, end - at + 1));
                    at = end + 1;
                }
                else
                {
                    at++;
                }
            }

            return nodes.Where(n => n is not null).Select(n => n!).ToList();
        }

        private static (string Segment, int Next) Segment(string query, int start)
        {
            if (start < query.Length && query[start] == '[')
            {
                var end = Close(query, start);
                return (query.Substring(start, end - start + 1), end + 1);
            }

            var at = start;
            while (at < query.Length && query[at] != '.' && query[at] != '[')
            {
                at++;
            }

            return (query.Substring(start, at - start), at);
        }

        private static List<JsonNode?> Step(List<JsonNode?> nodes, string segment)
        {
            if (!segment.StartsWith("[", StringComparison.Ordinal))
            {
                return segment == "*" ? nodes.SelectMany(Children).ToList() : nodes.Select(n => (n as JsonObject)?[segment]).ToList();
            }

            var inside = segment.Substring(1, segment.Length - 2).Trim();
            if (inside == "*")
            {
                return nodes.SelectMany(Children).ToList();
            }

            if (inside.StartsWith("?", StringComparison.Ordinal))
            {
                var filter = inside.Substring(1).Trim();
                return nodes.SelectMany(Children).Where(child => Truthy(new Filter(filter, child).Evaluate())).ToList();
            }

            var names = inside.Split(',').Select(n => n.Trim()).ToList();
            return nodes.SelectMany(n => names.Select(name =>
                name.StartsWith("'", StringComparison.Ordinal) || name.StartsWith("\"", StringComparison.Ordinal)
                    ? (n as JsonObject)?[name.Substring(1, name.Length - 2)]
                    : int.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) && n is JsonArray array
                        ? (index < 0 ? index + array.Count : index) is var i && i >= 0 && i < array.Count ? array[i] : null
                        : null)).ToList();
        }

        private static IEnumerable<JsonNode?> Children(JsonNode? node) => node switch
        {
            JsonObject o => o.Select(p => p.Value),
            JsonArray a => a,
            _ => Enumerable.Empty<JsonNode?>(),
        };

        private static IEnumerable<JsonNode?> Descendants(JsonNode? node)
        {
            yield return node;
            foreach (var child in Children(node))
            {
                foreach (var descendant in Descendants(child))
                {
                    yield return descendant;
                }
            }
        }

        private static int Close(string text, int open)
        {
            var depth = 0;
            var quote = '\0';
            for (var i = open; i < text.Length; i++)
            {
                var c = text[i];
                if (quote != '\0')
                {
                    quote = c == quote ? '\0' : quote;
                    continue;
                }

                if (c is '\'' or '"')
                {
                    quote = c;
                }
                else if (c == '[')
                {
                    depth++;
                }
                else if (c == ']' && --depth == 0)
                {
                    return i;
                }
            }

            return text.Length - 1;
        }

        internal static bool Truthy(object? value) => value switch
        {
            null => false,
            bool b => b,
            JsonValue v when v.GetValueKind() == JsonValueKind.False || v.GetValueKind() == JsonValueKind.Null => false,
            List<JsonNode> list => list.Count > 0,
            _ => true,
        };

        /// <summary>A filter expression: comparisons, <c>&amp;&amp;</c>, <c>||</c>, <c>!</c>, parentheses, and @-relative queries (non-empty is true).</summary>
        private sealed class Filter
        {
            private readonly string _text;
            private readonly JsonNode? _current;
            private int _at;

            public Filter(string text, JsonNode? current)
            {
                _text = text;
                _current = current;
            }

            public object? Evaluate() => Or();

            private object? Or()
            {
                var left = And();
                while (Take("||"))
                {
                    var right = And();
                    left = Truthy(left) || Truthy(right);
                }

                return left;
            }

            private object? And()
            {
                var left = Unary();
                while (Take("&&"))
                {
                    var right = Unary();
                    left = Truthy(left) && Truthy(right);
                }

                return left;
            }

            private object? Unary()
            {
                Skip();
                if (_at < _text.Length && _text[_at] == '!' && !(_at + 1 < _text.Length && _text[_at + 1] == '='))
                {
                    _at++;
                    return !Truthy(Unary());
                }

                var left = Primary();
                foreach (var op in new[] { "==", "!=", "<=", ">=", "<", ">" })
                {
                    if (Take(op))
                    {
                        return Compare(Value(left), op, Value(Primary()));
                    }
                }

                return left;
            }

            private object? Primary()
            {
                Skip();
                if (Take("("))
                {
                    var inner = Or();
                    Take(")");
                    return inner;
                }

                if (_at < _text.Length && (_text[_at] == '\'' || _text[_at] == '"'))
                {
                    var quote = _text[_at];
                    var end = _text.IndexOf(quote, _at + 1);
                    var literal = _text.Substring(_at + 1, end - _at - 1);
                    _at = end + 1;
                    return JsonValue.Create(literal);
                }

                var start = _at;
                while (_at < _text.Length && !char.IsWhiteSpace(_text[_at]) && "()=!<>&|".IndexOf(_text[_at]) < 0)
                {
                    _at += _text[_at] == '[' ? Close(_text, _at) - _at + 1 : 1;
                }

                var token = _text.Substring(start, _at - start);
                if (token.StartsWith("@", StringComparison.Ordinal) || token.StartsWith("$", StringComparison.Ordinal))
                {
                    return _current is null ? new List<JsonNode>() : Select(_current, token.Substring(1).Length == 0 ? "$" : "$" + token.Substring(1));
                }

                return token switch
                {
                    "true" => JsonValue.Create(true),
                    "false" => JsonValue.Create(false),
                    "null" => null,
                    _ => decimal.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? JsonValue.Create(n) : JsonValue.Create(token),
                };
            }

            /// <summary>A query used as a value is its single node (RFC 9535 comparisons take singular queries).</summary>
            private static JsonNode? Value(object? value) => value is List<JsonNode> list ? list.FirstOrDefault() : value as JsonNode;

            private static bool Compare(JsonNode? left, string op, JsonNode? right)
            {
                if (left is JsonValue l && right is JsonValue r && l.GetValueKind() == JsonValueKind.Number && r.GetValueKind() == JsonValueKind.Number
                    && decimal.TryParse(l.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var a)
                    && decimal.TryParse(r.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var b))
                {
                    return op switch { "==" => a == b, "!=" => a != b, "<" => a < b, "<=" => a <= b, ">" => a > b, _ => a >= b };
                }

                var equal = JsonNode.DeepEquals(left, right);
                var order = string.CompareOrdinal(left?.ToJsonString(), right?.ToJsonString());
                return op switch { "==" => equal, "!=" => !equal, "<" => order < 0, "<=" => order <= 0, ">" => order > 0, _ => order >= 0 };
            }

            private bool Take(string token)
            {
                Skip();
                if (string.CompareOrdinal(_text, _at, token, 0, token.Length) != 0)
                {
                    return false;
                }

                _at += token.Length;
                return true;
            }

            private void Skip()
            {
                while (_at < _text.Length && char.IsWhiteSpace(_text[_at]))
                {
                    _at++;
                }
            }
        }
    }
}
