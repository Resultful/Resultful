using System;
using System.Collections.Generic;
using System.Linq;
using CsCheck;
using NUnit.Framework;

namespace Resultful.Tests
{
    // M2 named methods (05). The laws say each named method agrees with Match on random results; the examples pin the
    // zero value and the argument checks.
    [TestFixture]
    public class NamedMethodsTests
    {
        static readonly Gen<Result<int, string>> GenResult =
            Gen.Select(Gen.Bool, Gen.Int, Gen.String, (ok, v, e) => ok ? v.Ok<int, string>() : e.Fail<int, string>());

        static readonly Gen<Result<int>> GenErrorList =
            Gen.Select(Gen.Bool, Gen.Int, Gen.String.Array[0, 3], (ok, v, e) => ok ? v.Ok() : e.Fail<int>());

        // The case, value and error as Match sees them: the reference every named method is held to.
        static (bool Ok, int Value, string? Error) Model(Result<int, string> r)
            => r.Match(v => (true, v, (string?)null), e => (false, 0, e));

        static (bool Ok, int Value, string[]? Errors) Model(Result<int> r)
            => r.Match(v => (true, v, (string[]?)null), e => (false, 0, e.ToArray()));

        static void Same((bool Ok, int Value, string[]? Errors) expected, (bool Ok, int Value, string[]? Errors) actual)
        {
            Assert.That(actual.Ok, Is.EqualTo(expected.Ok));
            Assert.That(actual.Value, Is.EqualTo(expected.Value));
            Assert.That(actual.Errors, Is.EqualTo(expected.Errors));
        }

        [Test]
        public void IsSuccess_and_IsFailure_agree_with_Match()
        {
            GenResult.Sample(r => r.IsSuccess == Model(r).Ok && r.IsFailure == !Model(r).Ok);
            GenErrorList.Sample(r => r.IsSuccess == Model(r).Ok && r.IsFailure == !Model(r).Ok);
        }

        [Test]
        public void Value_and_Error_agree_with_Match_and_throw_on_the_other_case()
        {
            GenResult.Sample(r =>
            {
                var m = Model(r);
                if (m.Ok)
                {
                    Assert.That(r.Value, Is.EqualTo(m.Value));
                    Assert.That(() => r.Error, Throws.InvalidOperationException.With.Message.Contains("success"));
                }
                else
                {
                    Assert.That(r.Error, Is.EqualTo(m.Error));
                    Assert.That(() => r.Value, Throws.InvalidOperationException.With.Message.Contains("failure"));
                }
            });
            GenErrorList.Sample(r =>
            {
                var m = Model(r);
                if (m.Ok) Assert.That(r.Value, Is.EqualTo(m.Value));
                else Assert.That(() => r.Value, Throws.InvalidOperationException.With.Message.Contains("failure"));
            });
        }

        [Test]
        public void TryGetValue_and_TryGetError_agree_with_Match()
        {
            GenResult.Sample(r =>
            {
                var m = Model(r);
                Assert.That(r.TryGetValue(out var value), Is.EqualTo(m.Ok));
                Assert.That(value, Is.EqualTo(m.Ok ? m.Value : default));
                Assert.That(r.TryGetError(out var error), Is.EqualTo(!m.Ok));
                Assert.That(error, Is.EqualTo(m.Error));
            });
            GenErrorList.Sample(r =>
            {
                var m = Model(r);
                Assert.That(r.TryGetValue(out var value), Is.EqualTo(m.Ok));
                Assert.That(value, Is.EqualTo(m.Ok ? m.Value : default));
            });
        }

        [Test]
        public void Deconstruct_agrees_with_Match()
        {
            GenResult.Sample(r =>
            {
                var (isSuccess, value, error) = r;
                Assert.That((isSuccess, value, error), Is.EqualTo(Model(r)));
            });
        }

        [Test]
        public void ValueOr_agrees_with_Match()
        {
            Gen.Select(GenResult, Gen.Int).Sample((r, fallback) => r.ValueOr(fallback) == r.Match(v => v, _ => fallback));
            Gen.Select(GenErrorList, Gen.Int).Sample((r, fallback) => r.ValueOr(fallback) == r.Match(v => v, _ => fallback));
        }

        [Test]
        public void OrElse_keeps_the_first_success()
        {
            Gen.Select(GenResult, GenResult).Sample((r, other) =>
                Model(r.OrElse(other)) == (Model(r).Ok ? Model(r) : Model(other)));
            Gen.Select(GenErrorList, GenErrorList).Sample((r, other) =>
                Same(Model(r).Ok ? Model(r) : Model(other), Model(r.OrElse(other))));
        }

        [Test]
        public void The_lazy_OrElse_runs_only_on_a_failure_and_gets_the_error()
        {
            Gen.Select(GenResult, GenResult).Sample((r, other) =>
            {
                var seen = new List<string>();
                var result = r.OrElse(e =>
                {
                    seen.Add(e);
                    return other;
                });
                var m = Model(r);
                Assert.That(Model(result), Is.EqualTo(m.Ok ? m : Model(other)));
                Assert.That(seen, Is.EqualTo(m.Ok ? Array.Empty<string>() : new[] { m.Error! }));
            });
        }

        [Test]
        public void The_lazy_OrElse_rejects_a_null_function()
        {
            Result<int, string> failed = "bad";
            Assert.That(() => failed.OrElse((Func<string, Result<int, string>>)null!), Throws.ArgumentNullException);
            Result<int, string> ok = 1;
            Assert.That(() => ok.OrElse((Func<string, Result<int, string>>)null!), Throws.ArgumentNullException);
        }

        // 0.2 behaviour, pinned until 08b: Fail accepts a null error (and Ok a null value), and the named members report
        // it as it is. This contradicts the target's [MemberNotNullWhen(true, nameof(Error))] on IsFailure (and the
        // Value half on IsSuccess). 08b makes Fail(null!) throw ArgumentNullException (ZERO-NULL-ERR) and changes this test.
        [Test]
        public void A_failure_holding_a_null_error_is_still_a_failure()
        {
            var r = ((string)null!).Fail<int, string>();
            Assert.That(r.IsFailure, Is.True);
            Assert.That(r.TryGetError(out var error), Is.True);
            Assert.That(error, Is.Null);
            Assert.That(r.ValueOr(7), Is.EqualTo(7));
        }

        // Decision 10's default for 05 (DECISIONS-TAKEN, awaiting the maintainer on #39): the zero value is neither a
        // success nor a failure, and every named method that reads the case throws. The 0.2 members keep treating it as
        // a success until 08b.
        [Test]
        public void The_zero_value_is_neither_case_and_reading_it_throws()
        {
            var zero = default(Result<int, string>);
            Assert.That(zero.IsSuccess, Is.False);
            Assert.That(zero.IsFailure, Is.False);
            Assert.That(() => zero.Value, Throws.InvalidOperationException.With.Message.Contains("zero value"));
            Assert.That(() => zero.Error, Throws.InvalidOperationException.With.Message.Contains("zero value"));
            Assert.That(() => zero.TryGetValue(out _), Throws.InvalidOperationException);
            Assert.That(() => zero.TryGetError(out _), Throws.InvalidOperationException);
            Assert.That(() => { var (_, _, _) = zero; }, Throws.InvalidOperationException);
            Assert.That(() => zero.OrElse(1.Ok<int, string>()), Throws.InvalidOperationException);
            Assert.That(() => zero.OrElse(_ => 1), Throws.InvalidOperationException);
            Assert.That(() => zero.ValueOr(1), Throws.InvalidOperationException);
            Assert.That(zero.Match(v => v, _ => -1), Is.EqualTo(0), "0.2's Match still sees a success (08b changes it)");

            var zeroList = default(Result<int>);
            Assert.That(zeroList.IsSuccess, Is.False);
            Assert.That(zeroList.IsFailure, Is.False);
            Assert.That(() => zeroList.Value, Throws.InvalidOperationException.With.Message.Contains("zero value"));
            Assert.That(() => zeroList.TryGetValue(out _), Throws.InvalidOperationException);
            Assert.That(() => zeroList.OrElse(1.Ok()), Throws.InvalidOperationException);
            Assert.That(() => zeroList.ValueOr(1), Throws.InvalidOperationException);
            Assert.That(zeroList.Match(v => v, _ => -1), Is.EqualTo(0), "0.2's Match still sees a success (08b changes it)");
        }

        [Test]
        public void A_zero_value_passed_as_the_fallback_result_is_returned_as_is()
        {
            Result<int, string> failed = "bad";
            Assert.That(failed.OrElse(default(Result<int, string>)).IsSuccess, Is.False);
            Result<int, string> ok = 1;
            Assert.That(ok.OrElse(default(Result<int, string>)).Value, Is.EqualTo(1));
        }
    }
}
