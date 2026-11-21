using FluentAssertions;
using FluentAssertions.Execution;

namespace Resultful.Assertions.FluentAssertions
{
    // The one place that talks to the library's assertion engine. ResultAssertions.cs is AwesomeAssertions' adapter's,
    // compiled here too; FluentAssertions 7 has no AssertionChain, so this goes through Execute.Assertion, which
    // FluentAssertions 8 removed: this adapter fails at run time there.
    internal readonly struct Check
    {
        // Execute.Assertion is the ambient AssertionScope, or a new one that throws. Its state resets after each
        // FailWith, so one is kept for an assertion and the error assertions chained after it.
        private readonly AssertionScope _scope;

        private Check(AssertionScope scope) => _scope = scope;

        public static Check Create() => new(Execute.Assertion);

        // Fails (throws, or records inside an AssertionScope) unless condition holds; returns condition.
        [CustomAssertion]
        public bool That(bool condition, string because, object[] becauseArgs, string message, params object[] args)
        {
            _scope.BecauseOf(because, becauseArgs).ForCondition(condition).FailWith(message, args);
            return condition;
        }

        // The library's own wildcard match, as its exception WithMessage uses (MatchEquivalentOf), run in a scope of its
        // own; returns its failures, already formatted, or none.
        [CustomAssertion]
        public static string[] Match(string actual, string pattern, string because, object[] becauseArgs, string identifier)
        {
            using var scope = new AssertionScope(identifier);
            actual.Should().MatchEquivalentOf(pattern, because, becauseArgs);
            return scope.Discard();
        }

        // Reports a failure the library already formatted.
        [CustomAssertion]
        public void Fail(string formatted) => _scope.FailWith(formatted.Replace("{", "{{").Replace("}", "}}"));

        // FluentAssertions 7's constraint takes no scope, so a follow-up on `.Which` is asserted on its own, even after
        // this assertion failed inside an AssertionScope; it names its subject from the source line. The signature is
        // the AwesomeAssertions adapter's.
        [CustomAssertion]
        public static AndWhichConstraint<TParent, TSubject> Which<TParent, TSubject>(Check check, TParent parent, TSubject subject, string postfix)
        {
            _ = check;
            _ = postfix;
            return new(parent, subject);
        }
    }
}
