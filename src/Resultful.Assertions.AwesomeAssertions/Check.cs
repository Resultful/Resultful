using AwesomeAssertions;
using AwesomeAssertions.Execution;

// The caller identifier ({context:result}) skips this assembly's frames, so it names the test's expression.
[assembly: CustomAssertionsAssembly]

namespace Resultful.Assertions.AwesomeAssertions
{
    // The one place that talks to the library's assertion engine; ResultAssertions.cs is shared with the FluentAssertions
    // adapter, whose Check.cs does the same through FluentAssertions 7's Execute.Assertion.
    internal readonly struct Check
    {
        private readonly AssertionChain _chain;

        private Check(AssertionChain chain) => _chain = chain;

        public static Check Create() => new(AssertionChain.GetOrCreate());

        // Fails (throws, or records inside an AssertionScope) unless condition holds; returns condition. Once a check on
        // this chain has failed, later ones are skipped, as the library's own chained assertions are.
        public bool That(bool condition, string because, object[] becauseArgs, string message, params object[] args)
        {
            _chain.BecauseOf(because, becauseArgs).ForCondition(condition).FailWith(message, args);
            return condition;
        }

        // The library's own wildcard match, as its exception WithMessage uses (MatchEquivalentOf), run in a scope of its
        // own; returns its failures, already formatted, or none.
        public static string[] Match(string actual, string pattern, string because, object[] becauseArgs, string identifier)
        {
            using var scope = new AssertionScope();
            var chain = AssertionChain.GetOrCreate();
            chain.OverrideCallerIdentifier(() => identifier);
            chain.ReuseOnce();
            actual.Should().MatchEquivalentOf(pattern, because, becauseArgs);
            return scope.Discard();
        }

        // Reports a failure the library already formatted.
        public void Fail(string formatted) => _chain.FailWith(formatted.Replace("{", "{{").Replace("}", "}}"));

        // The constraint carries the chain: `.Which.Should()` names its subject "result.Value" and, when this chain has
        // failed inside an AssertionScope, is skipped rather than reported a second time.
        public static AndWhichConstraint<TParent, TSubject> Which<TParent, TSubject>(Check check, TParent parent, TSubject subject, string postfix)
            => new(parent, subject, check._chain, postfix);
    }
}
