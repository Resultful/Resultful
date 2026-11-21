using System;
using System.Collections.Generic;
using System.Linq;
using Resultful;
using Resultful.Assertions;
#if RESULTFUL_FLUENTASSERTIONS
using FluentAssertions;
#else
using AwesomeAssertions;
#endif

// Decision 18: one source for both adapters. The Resultful.Assertions.FluentAssertions project compiles this file with
// RESULTFUL_FLUENTASSERTIONS; only the namespaces and Check.cs (the library's assertion engine) differ.
#if RESULTFUL_FLUENTASSERTIONS
namespace Resultful.Assertions.FluentAssertions
#else
namespace Resultful.Assertions.AwesomeAssertions
#endif
{
    /// <summary>
    /// Assertions on a <see cref="Result{T, TError}"/>, a <see cref="Result{T}"/> or a generated result shape, from
    /// <c>result.Should()</c>.
    /// </summary>
    /// <typeparam name="T">The type of the success value.</typeparam>
    /// <typeparam name="TError">The type of the error: <see cref="Error"/> for a <see cref="Result{T}"/>.</typeparam>
    /// <remarks>
    /// <para>
    /// A failure message prints an error with <see cref="Error.ToString"/>, never a derived record's own properties,
    /// and a success value by its runtime type only.
    /// </para>
    /// <para>
    /// Inside an <c>AssertionScope</c> a failure is recorded and the assertion returns a default subject, and the error
    /// assertions chained on it are skipped. With AwesomeAssertions an assertion made on <c>Which</c> is skipped too,
    /// since it goes through the same chain. FluentAssertions 7 can't skip it: <c>Succeed().Which.Should().Be(5)</c>
    /// on a failure reports the failure and then "Expected … to be 5, but found 0".
    /// </para>
    /// </remarks>
    public sealed class ResultAssertions<T, TError>
    {
        private const string Zero = "it is the zero value (default), which is neither a success nor a failure.";

        private readonly bool _isSuccess;
        private readonly bool _isFailure;
        private readonly T _value;
        private readonly TError _error;

        internal ResultAssertions(bool isSuccess, bool isFailure, T value, TError error)
        {
            _isSuccess = isSuccess;
            _isFailure = isFailure;
            _value = value;
            _error = error;
        }

        /// <summary>Asserts that the result is a success.</summary>
        /// <param name="because">A phrase explaining why, formatted with <paramref name="becauseArgs"/>; "because" is added if it doesn't start with it.</param>
        /// <param name="becauseArgs">Values for the placeholders in <paramref name="because"/>.</param>
        /// <returns>A constraint whose <c>Which</c> is the success value.</returns>
        [CustomAssertion]
        public AndWhichConstraint<ResultAssertions<T, TError>, T> Succeed(string because = "", params object[] becauseArgs)
        {
            var check = Check.Create();
            if (_isFailure)
            {
                check.That(false, because, becauseArgs, "Expected {context:result} to succeed{reason}, but it failed with {0}.", ResultInspection.DescribeError(_error));
            }
            else
            {
                check.That(_isSuccess, because, becauseArgs, "Expected {context:result} to succeed{reason}, but " + Zero);
            }

            return Check.Which(check, this, _value, ".Value");
        }

        /// <summary>Asserts that the result is a failure.</summary>
        /// <param name="because">A phrase explaining why, formatted with <paramref name="becauseArgs"/>; "because" is added if it doesn't start with it.</param>
        /// <param name="becauseArgs">Values for the placeholders in <paramref name="because"/>.</param>
        /// <returns>Assertions on the error, whose <see cref="ErrorAssertions{TError}.Which"/> is the error.</returns>
        [CustomAssertion]
        public ErrorAssertions<TError> Fail(string because = "", params object[] becauseArgs)
        {
            var check = Check.Create();
            var failed = check.That(_isFailure, because, becauseArgs, "Expected {context:result} to fail{reason}, but " + (_isSuccess ? ResultInspection.DescribeSuccess(_value) : Zero));
            return new ErrorAssertions<TError>(check, failed, _error);
        }

        /// <summary>
        /// Asserts that the result is a failure whose error is, or contains, a <typeparamref name="TLeaf"/>: the error
        /// itself, its cause chain or its inner errors, as <see cref="Error.TryFind{TError}"/> searches them, starting
        /// from the held error for an <see cref="IErrorUnion"/>.
        /// </summary>
        /// <typeparam name="TLeaf">The type of error expected: a closed hierarchy's leaf, a union case, or any error.</typeparam>
        /// <param name="because">A phrase explaining why, formatted with <paramref name="becauseArgs"/>; "because" is added if it doesn't start with it.</param>
        /// <param name="becauseArgs">Values for the placeholders in <paramref name="because"/>.</param>
        /// <returns>Assertions on the first <typeparamref name="TLeaf"/> found.</returns>
        [CustomAssertion]
        public ErrorAssertions<TLeaf> FailWith<TLeaf>(string because = "", params object[] becauseArgs)
            where TLeaf : Error
        {
            var check = Check.Create();
            var expected = "Expected {context:result} to fail with " + ResultInspection.TypeName(typeof(TLeaf)) + "{reason}, but ";
            TLeaf? leaf = null;
            bool found;
            if (_isFailure)
            {
                found = check.That(ResultInspection.TryFindError(_error, out leaf), because, becauseArgs, expected + "it failed with {0}.", ResultInspection.DescribeError(_error));
            }
            else
            {
                found = check.That(false, because, becauseArgs, expected + (_isSuccess ? ResultInspection.DescribeSuccess(_value) : Zero));
            }

            return new ErrorAssertions<TLeaf>(check, found, leaf!);
        }
    }

    /// <summary>
    /// Assertions on a <see cref="Validation{T, TError}"/>, a <see cref="Validation{T}"/> or a gathering generated shape,
    /// from <c>validation.Should()</c>.
    /// </summary>
    /// <typeparam name="T">The type of the success value.</typeparam>
    /// <typeparam name="TError">The type of the errors: <see cref="Error"/> for a <see cref="Validation{T}"/>.</typeparam>
    /// <remarks>Failure messages print each error with <see cref="Error.ToString"/>, and a success value by its runtime type only.</remarks>
    public sealed class ValidationAssertions<T, TError>
    {
        private const string Zero = "it is the zero value (default), which is neither a success nor a failure.";

        private readonly bool _isSuccess;
        private readonly IReadOnlyList<TError>? _errors;
        private readonly T _value;

        internal ValidationAssertions(bool isSuccess, T value, IReadOnlyList<TError>? errors)
        {
            _isSuccess = isSuccess;
            _value = value;
            _errors = errors;
        }

        /// <summary>Asserts that the validation is a success.</summary>
        /// <param name="because">A phrase explaining why, formatted with <paramref name="becauseArgs"/>; "because" is added if it doesn't start with it.</param>
        /// <param name="becauseArgs">Values for the placeholders in <paramref name="because"/>.</param>
        /// <returns>A constraint whose <c>Which</c> is the success value.</returns>
        [CustomAssertion]
        public AndWhichConstraint<ValidationAssertions<T, TError>, T> Succeed(string because = "", params object[] becauseArgs)
        {
            var check = Check.Create();
            if (_errors is not null)
            {
                check.That(false, because, becauseArgs, "Expected {context:validation} to succeed{reason}, but it failed with {0}.", (object)Describe(_errors));
            }
            else
            {
                check.That(_isSuccess, because, becauseArgs, "Expected {context:validation} to succeed{reason}, but " + Zero);
            }

            return Check.Which(check, this, _value, ".Value");
        }

        /// <summary>Asserts that the validation is a failure.</summary>
        /// <param name="because">A phrase explaining why, formatted with <paramref name="becauseArgs"/>; "because" is added if it doesn't start with it.</param>
        /// <param name="becauseArgs">Values for the placeholders in <paramref name="because"/>.</param>
        /// <returns>A constraint whose <c>Which</c> is the errors, in order; empty when the assertion failed inside an <c>AssertionScope</c>.</returns>
        [CustomAssertion]
        public AndWhichConstraint<ValidationAssertions<T, TError>, IReadOnlyList<TError>> Fail(string because = "", params object[] becauseArgs)
        {
            var check = Check.Create();
            check.That(_errors is not null, because, becauseArgs, "Expected {context:validation} to fail{reason}, but " + (_isSuccess ? ResultInspection.DescribeSuccess(_value) : Zero));
            return Check.Which(check, this, _errors ?? Array.Empty<TError>(), ".Errors");
        }

        /// <summary>
        /// Asserts that the validation is a failure, and one of its errors is, or contains, a <typeparamref name="TLeaf"/>,
        /// searching the errors in order as <see cref="Error.TryFind{TError}"/> searches each.
        /// </summary>
        /// <typeparam name="TLeaf">The type of error expected.</typeparam>
        /// <param name="because">A phrase explaining why, formatted with <paramref name="becauseArgs"/>; "because" is added if it doesn't start with it.</param>
        /// <param name="becauseArgs">Values for the placeholders in <paramref name="because"/>.</param>
        /// <returns>Assertions on the first <typeparamref name="TLeaf"/> found.</returns>
        [CustomAssertion]
        public ErrorAssertions<TLeaf> FailWith<TLeaf>(string because = "", params object[] becauseArgs)
            where TLeaf : Error
        {
            var check = Check.Create();
            var expected = "Expected {context:validation} to fail with " + ResultInspection.TypeName(typeof(TLeaf)) + "{reason}, but ";
            TLeaf? leaf = null;
            bool found;
            if (_errors is not null)
            {
                found = check.That(ResultInspection.TryFindInErrors(_errors, out leaf), because, becauseArgs, expected + "it failed with {0}.", (object)Describe(_errors));
            }
            else
            {
                found = check.That(false, because, becauseArgs, expected + (_isSuccess ? ResultInspection.DescribeSuccess(_value) : Zero));
            }

            return new ErrorAssertions<TLeaf>(check, found, leaf!);
        }

        private static string[] Describe(IReadOnlyList<TError> errors) => errors.Select(ResultInspection.DescribeError).ToArray();
    }

    /// <summary>Assertions on an <see cref="Option{T}"/>, from <c>option.Should()</c>. A value is never printed.</summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    public sealed class OptionAssertions<T>
    {
        private readonly bool _isSome;
        private readonly T _value;

        internal OptionAssertions(Option<T> option)
        {
            (_isSome, _value) = option.Match(v => (true, v), _ => (false, default(T)!));
        }

        /// <summary>Asserts that the option has a value.</summary>
        /// <param name="because">A phrase explaining why, formatted with <paramref name="becauseArgs"/>; "because" is added if it doesn't start with it.</param>
        /// <param name="becauseArgs">Values for the placeholders in <paramref name="because"/>.</param>
        /// <returns>A constraint whose <c>Which</c> is the value.</returns>
        [CustomAssertion]
        public AndWhichConstraint<OptionAssertions<T>, T> BeSome(string because = "", params object[] becauseArgs)
        {
            var check = Check.Create();
            check.That(_isSome, because, becauseArgs, "Expected {context:option} to be Some{reason}, but it was None.");
            return Check.Which(check, this, _value, ".Value");
        }

        /// <summary>Asserts that the option has no value.</summary>
        /// <param name="because">A phrase explaining why, formatted with <paramref name="becauseArgs"/>; "because" is added if it doesn't start with it.</param>
        /// <param name="becauseArgs">Values for the placeholders in <paramref name="because"/>.</param>
        /// <returns>A constraint to chain more assertions on the option.</returns>
        [CustomAssertion]
        public AndConstraint<OptionAssertions<T>> BeNone(string because = "", params object[] becauseArgs)
        {
            Check.Create().That(!_isSome, because, becauseArgs, "Expected {context:option} to be None{reason}, but it was Some.");
            return new AndConstraint<OptionAssertions<T>>(this);
        }
    }

    /// <summary>
    /// Assertions on the error of a failure, from <c>Fail()</c> or <c>FailWith&lt;TLeaf&gt;()</c>: its message, its
    /// context notes and the errors under it.
    /// </summary>
    /// <typeparam name="TError">The type of the error.</typeparam>
    /// <remarks>
    /// An <see cref="IErrorUnion"/> is read through the error it holds. When the assertion that produced these failed
    /// inside an <c>AssertionScope</c>, there is no error and every assertion here is skipped, so the scope reports the
    /// first failure only.
    /// With AwesomeAssertions so is an assertion made on <see cref="Which"/>, which goes through the same chain;
    /// FluentAssertions 7 can't skip that one, so it is asserted on the <c>default</c> subject and reported too.
    /// </remarks>
    public sealed class ErrorAssertions<TError>
    {
        private readonly Check _check;
        private readonly bool _present;
        private readonly TError _error;
        private readonly AndWhichConstraint<ErrorAssertions<TError>, TError> _which;

        internal ErrorAssertions(Check check, bool present, TError error)
        {
            _check = check;
            _present = present;
            _error = error;
            _which = Check.Which(check, this, error, ".Error");
        }

        /// <summary>The error; <c>default</c> when the assertion that produced it failed inside an <c>AssertionScope</c>.</summary>
        public TError Which => _which.Which;

        /// <summary>
        /// Asserts that the error's <see cref="Error.Message"/> matches a wildcard pattern (<c>*</c> for any run of
        /// characters, <c>?</c> for one) the way the library's <c>WithMessage</c> on an exception does: through its own
        /// <c>MatchEquivalentOf</c>, so case is ignored.
        /// </summary>
        /// <param name="expectedWildcardPattern">The pattern, such as <c>"order * was not found"</c>.</param>
        /// <param name="because">A phrase explaining why, formatted with <paramref name="becauseArgs"/>; "because" is added if it doesn't start with it.</param>
        /// <param name="becauseArgs">Values for the placeholders in <paramref name="because"/>.</param>
        /// <returns>These assertions, to chain more.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="expectedWildcardPattern"/> is <c>null</c>.</exception>
        [CustomAssertion]
        public ErrorAssertions<TError> WithMessage(string expectedWildcardPattern, string because = "", params object[] becauseArgs)
        {
            _ = expectedWildcardPattern ?? throw new ArgumentNullException(nameof(expectedWildcardPattern));
            if (AsError("to have a message matching {0}", because, becauseArgs, expectedWildcardPattern) is { } error)
            {
                foreach (var failure in Check.Match(error.Message, expectedWildcardPattern, because, becauseArgs, "error message"))
                {
                    _check.Fail(failure);
                }
            }

            return this;
        }

        /// <summary>Asserts that one of the error's <see cref="Error.Context"/> notes matches a wildcard pattern, as <see cref="WithMessage"/> matches.</summary>
        /// <param name="expectedWildcardPattern">The note, or a pattern for it.</param>
        /// <param name="because">A phrase explaining why, formatted with <paramref name="becauseArgs"/>; "because" is added if it doesn't start with it.</param>
        /// <param name="becauseArgs">Values for the placeholders in <paramref name="because"/>.</param>
        /// <returns>These assertions, to chain more.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="expectedWildcardPattern"/> is <c>null</c>.</exception>
        [CustomAssertion]
        public ErrorAssertions<TError> WithContext(string expectedWildcardPattern, string because = "", params object[] becauseArgs)
        {
            _ = expectedWildcardPattern ?? throw new ArgumentNullException(nameof(expectedWildcardPattern));
            if (AsError("to have a context note matching {0}", because, becauseArgs, expectedWildcardPattern) is { } error)
            {
                _check.That(
                    error.Context.Any(note => Check.Match(note, expectedWildcardPattern, string.Empty, [], "context note").Length == 0),
                    because,
                    becauseArgs,
                    "Expected the error to have a context note matching {0}{reason}, but found {1}.",
                    expectedWildcardPattern,
                    error.Context.ToArray());
            }

            return this;
        }

        /// <summary>
        /// Asserts that something under the error is a <typeparamref name="TCause"/>: its <see cref="Error.Cause"/>
        /// chain or its inner errors, as <see cref="Error.Causes"/> lists them, not the error itself.
        /// </summary>
        /// <typeparam name="TCause">The type of the lower error expected.</typeparam>
        /// <param name="because">A phrase explaining why, formatted with <paramref name="becauseArgs"/>; "because" is added if it doesn't start with it.</param>
        /// <param name="becauseArgs">Values for the placeholders in <paramref name="because"/>.</param>
        /// <returns>Assertions on the first <typeparamref name="TCause"/> under the error.</returns>
        [CustomAssertion]
        public ErrorAssertions<TCause> WithCause<TCause>(string because = "", params object[] becauseArgs)
            where TCause : Error
        {
            var expectation = "to have a cause of type " + ResultInspection.TypeName(typeof(TCause));
            TCause? cause = null;
            var found = false;
            if (AsError(expectation, because, becauseArgs) is { } error)
            {
                cause = error.Causes().Skip(1).OfType<TCause>().FirstOrDefault();
                found = _check.That(cause is not null, because, becauseArgs, "Expected the error " + expectation + "{reason}, but found {0}.", error.ToString());
            }

            return new ErrorAssertions<TCause>(_check, found, cause!);
        }

        /// <summary>Asserts that the error is, or contains, a <typeparamref name="TLeaf"/>, as <see cref="Error.TryFind{TError}"/> searches.</summary>
        /// <typeparam name="TLeaf">The type of error expected.</typeparam>
        /// <param name="because">A phrase explaining why, formatted with <paramref name="becauseArgs"/>; "because" is added if it doesn't start with it.</param>
        /// <param name="becauseArgs">Values for the placeholders in <paramref name="because"/>.</param>
        /// <returns>Assertions on the first <typeparamref name="TLeaf"/> found.</returns>
        [CustomAssertion]
        public ErrorAssertions<TLeaf> WithError<TLeaf>(string because = "", params object[] becauseArgs)
            where TLeaf : Error
        {
            var expectation = "to be or contain " + ResultInspection.TypeName(typeof(TLeaf));
            TLeaf? leaf = null;
            var found = false;
            if (AsError(expectation, because, becauseArgs) is { } error)
            {
                found = _check.That(error.TryFind(out leaf), because, becauseArgs, "Expected the error " + expectation + "{reason}, but found {0}.", error.ToString());
            }

            return new ErrorAssertions<TLeaf>(_check, found, leaf!);
        }

        // The error to assert on, or null: when there is none (an earlier failure in a scope) nothing is reported again,
        // and when it isn't an Error or a union of them, that is the failure.
        [CustomAssertion]
        private Error? AsError(string expectation, string because, object[] becauseArgs, params object[] args)
        {
            if (!_present)
            {
                return null;
            }

            var error = ResultInspection.AsError(_error);
            if (error is null)
            {
                var actual = _error is null ? "null" : "a " + ResultInspection.TypeName(_error.GetType()) + ", not an Error";
                _check.That(false, because, becauseArgs, "Expected the error " + expectation + "{reason}, but it is " + actual + ".", args);
            }

            return error;
        }
    }
}
