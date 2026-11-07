using System.Globalization;

namespace Matchmark.Rules;

/// <summary>
/// IDL, the Inter-parameter Dependency Language (Martín-López, Segura et al.), as embedded in OpenAPI with
/// <c>x-dependencies</c> on an operation: <c>IF p THEN q;</c>, <c>Or(...)</c>, <c>OnlyOne(...)</c>, <c>AllOrNone(...)</c>,
/// <c>ZeroOrOne(...)</c>, and relational (<c>from &lt;= to</c>) and arithmetic (<c>a + b &lt;= c</c>) dependencies. Predicates
/// are a parameter being present, <c>p == 'v'</c> (or <c>'a'|'b'</c>), <c>p != 'v'</c>, <c>NOT</c>, <c>AND</c>, <c>OR</c>.
/// A dependency can be checked against a request's parameters, satisfied (to make a valid request), or broken (to make
/// an invalid one).
/// </summary>
public sealed class Idl
{
    private Idl(string source, Node root)
    {
        Source = source;
        Root = root;
    }

    public string Source { get; }

    private Node Root { get; }

    /// <summary>The parameter names the dependency is about.</summary>
    public IReadOnlyList<string> Parameters => Root.Names().Distinct().ToList();

    public static Idl Parse(string dependency)
    {
        var parser = new Parser(dependency.Trim().TrimEnd(';'));
        return new Idl(dependency.Trim(), parser.Dependency());
    }

    public bool Holds(IReadOnlyDictionary<string, string> parameters) => Root.Holds(parameters);

    /// <summary>
    /// The parameters changed (values from <paramref name="sample"/>, which gives a valid value for a name) so the
    /// dependency holds, or null if that can't be done.
    /// </summary>
    public Dictionary<string, string>? Satisfy(IReadOnlyDictionary<string, string> parameters, Func<string, string?> sample)
    {
        var copy = new Dictionary<string, string>(parameters, StringComparer.Ordinal);
        return Root.Holds(copy) || Root.Make(copy, true, sample) && Root.Holds(copy) ? copy : null;
    }

    /// <summary>The parameters changed so the dependency is broken, or null if that can't be done with valid values.</summary>
    public Dictionary<string, string>? Break(IReadOnlyDictionary<string, string> parameters, Func<string, string?> sample)
    {
        var copy = new Dictionary<string, string>(parameters, StringComparer.Ordinal);
        return Root.Make(copy, false, sample) && !Root.Holds(copy) ? copy : null;
    }

    public override string ToString() => Source;

    private abstract class Node
    {
        public abstract bool Holds(IReadOnlyDictionary<string, string> p);

        /// <summary>Changes the parameters so this node is <paramref name="truth"/>; false if it can't.</summary>
        public abstract bool Make(Dictionary<string, string> p, bool truth, Func<string, string?> sample);

        public abstract IEnumerable<string> Names();
    }

    private sealed class Present(string name) : Node
    {
        public override bool Holds(IReadOnlyDictionary<string, string> p) => p.ContainsKey(name);

        public override bool Make(Dictionary<string, string> p, bool truth, Func<string, string?> sample)
        {
            if (!truth)
            {
                p.Remove(name);
                return true;
            }

            if (p.ContainsKey(name))
            {
                return true;
            }

            return sample(name) is { } value && (p[name] = value) is not null;
        }

        public override IEnumerable<string> Names() => [name];
    }

    private sealed class Is(string name, IReadOnlyList<string> values, bool negated) : Node
    {
        public override bool Holds(IReadOnlyDictionary<string, string> p)
            => p.TryGetValue(name, out var v) ? values.Contains(v) != negated : negated;

        public override bool Make(Dictionary<string, string> p, bool truth, Func<string, string?> sample)
        {
            if (truth != negated)
            {
                p[name] = values[0];
                return true;
            }

            p.Remove(name);
            return true;
        }

        public override IEnumerable<string> Names() => [name];
    }

    private sealed class Not(Node inner) : Node
    {
        public override bool Holds(IReadOnlyDictionary<string, string> p) => !inner.Holds(p);

        public override bool Make(Dictionary<string, string> p, bool truth, Func<string, string?> sample) => inner.Make(p, !truth, sample);

        public override IEnumerable<string> Names() => inner.Names();
    }

    /// <summary>AND, OR, and the group dependencies: how many of the predicates must hold.</summary>
    private sealed class Group(string kind, IReadOnlyList<Node> items) : Node
    {
        public override bool Holds(IReadOnlyDictionary<string, string> p)
        {
            var count = items.Count(i => i.Holds(p));
            return kind switch
            {
                "AND" => count == items.Count,
                "OR" or "Or" => count >= 1,
                "OnlyOne" => count == 1,
                "AllOrNone" => count == 0 || count == items.Count,
                _ => count <= 1, // ZeroOrOne
            };
        }

        public override bool Make(Dictionary<string, string> p, bool truth, Func<string, string?> sample)
        {
            // The pattern of which predicates hold: first true and the rest false, all, none, or the first two.
            bool[] pattern = (kind, truth) switch
            {
                ("AND", true) or ("AllOrNone", true) => items.Select(_ => true).ToArray(),
                ("AND", false) or ("AllOrNone", false) => items.Select((_, i) => i == 0).ToArray(),
                ("OR" or "Or", true) or ("OnlyOne", true) or ("ZeroOrOne", true) => items.Select((_, i) => i == 0).ToArray(),
                ("OR" or "Or", false) => items.Select(_ => false).ToArray(),
                _ => items.Select((_, i) => i < 2).ToArray(), // OnlyOne or ZeroOrOne broken: two at once
            };
            return items.Select((item, i) => item.Make(p, pattern[i], sample)).ToList().All(made => made);
        }

        public override IEnumerable<string> Names() => items.SelectMany(i => i.Names());
    }

    private sealed class Requires(Node condition, Node consequence) : Node
    {
        public override bool Holds(IReadOnlyDictionary<string, string> p) => !condition.Holds(p) || consequence.Holds(p);

        public override bool Make(Dictionary<string, string> p, bool truth, Func<string, string?> sample)
            => truth
                ? condition.Holds(p) ? consequence.Make(p, true, sample) : true
                : condition.Make(p, true, sample) && consequence.Make(p, false, sample);

        public override IEnumerable<string> Names() => condition.Names().Concat(consequence.Names());
    }

    /// <summary>A relational or arithmetic dependency between numeric parameters: <c>a + b &lt;= c</c>.</summary>
    private sealed class Relation(IReadOnlyList<(char Sign, string Name)> left, string op, IReadOnlyList<(char Sign, string Name)> right) : Node
    {
        public override bool Holds(IReadOnlyDictionary<string, string> p)
        {
            if (Sum(left, p) is not { } l || Sum(right, p) is not { } r)
            {
                return true; // a relation only constrains parameters that are there
            }

            return op switch { "<" => l < r, "<=" => l <= r, ">" => l > r, ">=" => l >= r, "==" => l == r, _ => l != r };
        }

        public override bool Make(Dictionary<string, string> p, bool truth, Func<string, string?> sample)
        {
            foreach (var (_, name) in left.Concat(right).Where(n => !p.ContainsKey(n.Name)))
            {
                if (sample(name) is not { } value)
                {
                    return false;
                }

                p[name] = value;
            }

            if (Holds(p) == truth)
            {
                return true;
            }

            // Move the first term on the left until the relation is as wanted.
            var (sign, first) = left[0];
            var l = Sum(left, p)!.Value;
            var r = Sum(right, p)!.Value;
            var current = decimal.Parse(p[first], NumberStyles.Float, CultureInfo.InvariantCulture);
            var wanted = (op, truth) switch
            {
                ("<" or "<=", true) or (">" or ">=", false) => r - (l - Signed(sign, current)) - (op is "<" || op is ">=" ? 1 : 0),
                ("==", true) or ("!=", false) => r - (l - Signed(sign, current)),
                ("!=", true) or ("==", false) => r - (l - Signed(sign, current)) + 1,
                _ => r - (l - Signed(sign, current)) + (op is ">" || op is "<=" ? 1 : 0),
            };
            p[first] = (sign == '-' ? -wanted : wanted).ToString(CultureInfo.InvariantCulture);
            return Holds(p) == truth;
        }

        public override IEnumerable<string> Names() => left.Concat(right).Select(n => n.Name);

        private static decimal Signed(char sign, decimal value) => sign == '-' ? -value : value;

        private static decimal? Sum(IReadOnlyList<(char Sign, string Name)> terms, IReadOnlyDictionary<string, string> p)
        {
            decimal total = 0;
            foreach (var (sign, name) in terms)
            {
                if (!p.TryGetValue(name, out var text) || !decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    return null;
                }

                total += Signed(sign, value);
            }

            return total;
        }
    }

    private sealed class Parser(string text)
    {
        private static readonly string[] Groups = ["OnlyOne", "AllOrNone", "ZeroOrOne", "Or"];
        private int _at;

        public Node Dependency()
        {
            Skip();
            if (TakeWord("IF"))
            {
                var condition = Predicate();
                Expect("THEN");
                return new Requires(condition, Predicate());
            }

            return Predicate();
        }

        private Node Predicate()
        {
            var left = Conjunction();
            while (TakeWord("OR") || Take("||"))
            {
                left = new Group("OR", [left, Conjunction()]);
            }

            return left;
        }

        private Node Conjunction()
        {
            var left = Term();
            while (TakeWord("AND") || Take("&&"))
            {
                left = new Group("AND", [left, Term()]);
            }

            return left;
        }

        private Node Term()
        {
            Skip();
            if (TakeWord("NOT") || Peek("!") && !Peek("!="))
            {
                if (Peek("!"))
                {
                    _at++;
                }

                return new Not(Term());
            }

            if (Take("("))
            {
                var inner = Predicate();
                Expect(")");
                return inner;
            }

            foreach (var kind in Groups)
            {
                if (TakeWord(kind))
                {
                    Expect("(");
                    var items = new List<Node> { Predicate() };
                    while (Take(","))
                    {
                        items.Add(Predicate());
                    }

                    Expect(")");
                    return new Group(kind, items);
                }
            }

            var terms = new List<(char, string)> { ('+', Name()) };
            while (Peek("+") || Peek("-") && !Peek("->"))
            {
                var sign = text[_at++];
                terms.Add((sign, Name()));
            }

            foreach (var op in new[] { "==", "!=", "<=", ">=", "<", ">" })
            {
                if (Take(op))
                {
                    Skip();
                    if (terms.Count == 1 && op is "==" or "!=" && _at < text.Length && (text[_at] is '\'' or '"' || char.IsDigit(text[_at]) || Peek("true") || Peek("false")))
                    {
                        var values = new List<string> { Literal() };
                        while (Take("|"))
                        {
                            values.Add(Literal());
                        }

                        return new Is(terms[0].Item2, values, op == "!=");
                    }

                    var right = new List<(char, string)> { ('+', Name()) };
                    while (Peek("+") || Peek("-"))
                    {
                        var sign = text[_at++];
                        right.Add((sign, Name()));
                    }

                    return new Relation(terms, op, right);
                }
            }

            return terms.Count == 1 ? new Present(terms[0].Item2) : throw new FormatException($"'{text}': an arithmetic term needs a comparison");
        }

        private string Literal()
        {
            Skip();
            if (text[_at] is '\'' or '"')
            {
                var quote = text[_at];
                var end = text.IndexOf(quote, _at + 1);
                var value = text[(_at + 1)..end];
                _at = end + 1;
                return value;
            }

            var start = _at;
            while (_at < text.Length && (char.IsLetterOrDigit(text[_at]) || text[_at] is '.' or '-'))
            {
                _at++;
            }

            return text[start.._at];
        }

        private string Name()
        {
            Skip();
            var start = _at;
            while (_at < text.Length && (char.IsLetterOrDigit(text[_at]) || text[_at] is '_' or '.' or '[' or ']' or '-' && _at + 1 < text.Length && char.IsLetter(text[_at + 1])))
            {
                _at++;
            }

            return _at > start ? text[start.._at] : throw new FormatException($"'{text}': expected a parameter at '{text[start..]}'");
        }

        private void Expect(string token)
        {
            if (!(char.IsLetter(token[0]) ? TakeWord(token) : Take(token)))
            {
                throw new FormatException($"'{text}': expected '{token}' at '{text[_at..]}'");
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
            if (!Peek(word) || _at + word.Length < text.Length && (char.IsLetterOrDigit(text[_at + word.Length]) || text[_at + word.Length] == '_'))
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
