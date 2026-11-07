namespace Matchmark.Fakes;

/// <summary>A fake did not receive the calls a test said it should.</summary>
public sealed class FakeAssertionException(string message) : Exception(message);

/// <summary>The calls a fake received, and which of them a test has already asserted on.</summary>
public sealed class FakeCallLog(Func<IReadOnlyList<FakeCall>> calls)
{
    private readonly HashSet<FakeCall> _asserted = new(ReferenceEqualityComparer.Instance);

    public IReadOnlyList<FakeCall> Calls => calls();

    internal void MarkAsserted(IEnumerable<FakeCall> asserted)
    {
        lock (_asserted)
        {
            _asserted.UnionWith(asserted);
        }
    }

    /// <summary>Fails unless the calls include ones matching each matcher, in this order (others may come between).</summary>
    public void HaveReceivedInOrder(IReadOnlyList<FakeCallMatcher> matchers)
    {
        var calls = Calls;
        var position = 0;
        var matched = new List<FakeCall>();
        for (var step = 0; step < matchers.Count; step++)
        {
            var matcher = matchers[step];
            var index = Enumerable.Range(position, calls.Count - position).FirstOrDefault(i => matcher.Matches(calls[i]), -1);
            if (index < 0)
            {
                var expected = string.Join(", then ", matchers.Select(m => m.Description));
                var why = step > 0 && calls.Take(position).Any(matcher.Matches)
                    ? $"{matcher} was only received before {matchers[step - 1]}"
                    : $"{matcher} was not received" + (step > 0 ? $" after {matchers[step - 1]}" : string.Empty);
                throw new FakeAssertionException($"Expected the fake to have received {expected}, but {why}.{Received(calls)}");
            }

            matched.Add(calls[index]);
            position = index + 1;
        }

        MarkAsserted(matched);
    }

    /// <summary>Fails if any call has not been asserted on, by <c>HaveReceived</c> or <c>HaveReceivedInOrder</c>.</summary>
    public void HaveReceivedNoOtherCalls()
    {
        List<FakeCall> others;
        lock (_asserted)
        {
            others = Calls.Where(c => !_asserted.Contains(c)).ToList();
        }

        if (others.Count > 0)
        {
            throw new FakeAssertionException(
                $"Expected the fake to have received no other calls, but it received {others.Count} more:"
                + string.Concat(others.Select(c => $"\n  {FakeCallMatcher.Describe(c)}")));
        }
    }

    internal static string Received(IReadOnlyList<FakeCall> calls) => calls.Count == 0
        ? "\nIt received no calls."
        : "\nIt received:" + string.Concat(calls.Select((c, i) => $"\n  {i + 1}. {FakeCallMatcher.Describe(c)}"));
}

/// <summary>The end of a <c>HaveReceived</c> chain: how many matching calls there should have been.</summary>
public sealed class FakeCallAssertion(FakeCallLog log, FakeCallMatcher matcher)
{
    public void Once() => Times(1);

    public void Twice() => Times(2);

    public void Never() => Times(0);

    public void AtLeastOnce() => AtLeast(1);

    public void Times(int expected) => Check(n => n == expected, Count(expected));

    public void AtLeast(int expected) => Check(n => n >= expected, $"at least {Count(expected)}");

    public void AtMost(int expected) => Check(n => n <= expected, $"at most {Count(expected)}");

    private void Check(Func<int, bool> ok, string expected)
    {
        var calls = log.Calls;
        var matching = calls.Where(matcher.Matches).ToList();
        if (!ok(matching.Count))
        {
            throw new FakeAssertionException(
                $"Expected the fake to have received {matcher} {expected}, but it received it {Count(matching.Count)}.{FakeCallLog.Received(calls)}");
        }

        log.MarkAsserted(matching);
    }

    private static string Count(int n) => n switch { 0 => "0 times", 1 => "once", 2 => "twice", _ => $"{n} times" };
}
