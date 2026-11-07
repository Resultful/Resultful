using System.Text.Json;

namespace Resultful.OpenApi.Fakes;

/// <summary>Which calls something applies to: an operation, and a test of its arguments. Its description reads like the call.</summary>
public sealed class FakeCallMatcher(string operation, string description, Func<FakeCall, bool> matches)
{
    public string Operation { get; } = operation;

    public string Description { get; } = description;

    public bool Matches(FakeCall call) => call.Operation == Operation && matches(call);

    public override string ToString() => Description;

    /// <summary>"getPet(petId: 7)": how a call or a matcher is shown in messages.</summary>
    public static string Describe(string operation, IEnumerable<(string Name, object? Value)> arguments)
        => $"{operation}({string.Join(", ", arguments.Select(a => $"{a.Name}: {Show(a.Value)}"))})";

    public static string Describe(FakeCall call) => Describe(call.Operation, call.Arguments.Select(a => (a.Key, a.Value)));

    private static string Show(object? value) => value switch
    {
        null => "null",
        string s => JsonSerializer.Serialize(s),
        IFormattable f when value.GetType().IsPrimitive => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => JsonSerializer.Serialize(value, value.GetType(), JsonSerializerOptions.Web),
    };
}

/// <summary>
/// One <c>Given</c>: the calls it applies to and the answers it gives, in order. After the last answer it keeps giving
/// the last one, so <c>Returns.Ok().Then.NotFound()</c> answers 200 once and 404 from then on.
/// </summary>
public sealed class FakeRule(FakeCallMatcher matcher)
{
    private readonly object _gate = new();
    private readonly List<Func<object?>> _answers = [];
    private int _given;

    public FakeCallMatcher Matcher { get; } = matcher;

    public bool HasAnswers
    {
        get
        {
            lock (_gate)
            {
                return _answers.Count > 0;
            }
        }
    }

    public void Add(Func<object?> answer)
    {
        lock (_gate)
        {
            _answers.Add(answer);
        }
    }

    public object? Next()
    {
        lock (_gate)
        {
            var answer = _answers[Math.Min(_given, _answers.Count - 1)];
            _given++;
            return answer();
        }
    }
}

/// <summary>A fake's rules. The latest matching rule wins, so a test can override an earlier, broader one.</summary>
public sealed class FakeRuleSet
{
    private readonly object _gate = new();
    private readonly List<FakeRule> _rules = [];

    /// <summary>Calls no rule matches fail, instead of getting the default answer.</summary>
    public bool NothingElse { get; set; }

    public FakeRule Add(FakeCallMatcher matcher)
    {
        var rule = new FakeRule(matcher);
        lock (_gate)
        {
            _rules.Add(rule);
        }

        return rule;
    }

    public FakeRule? Find(FakeCall call)
    {
        lock (_gate)
        {
            for (var i = _rules.Count - 1; i >= 0; i--)
            {
                if (_rules[i].HasAnswers && _rules[i].Matcher.Matches(call))
                {
                    return _rules[i];
                }
            }

            return null;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _rules.Clear();
            NothingElse = false;
        }
    }
}
