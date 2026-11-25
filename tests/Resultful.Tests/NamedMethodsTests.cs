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

        // 08b (ZERO-NULL-ERR): a failure always holds an error, so Fail(null!) throws, which keeps
        // [MemberNotNullWhen(true, nameof(Error))] on IsFailure true. 0.2 gave a failure holding null. A success may hold
        // null: Ok<string?>(null) is a success whose Value is null.
        [Test]
        public void Fail_rejects_a_null_error_and_Ok_accepts_a_null_value()
        {
            Assert.That(() => ((string)null!).Fail<int, string>(), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("value"));
            Assert.That(() => ((string)null!).Fail(), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("value"));
            Assert.That(() => { Result<int, string> r = (string)null!; }, Throws.ArgumentNullException);
            Assert.That(() => Result.Fail<int>((IEnumerable<string>)null!), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("errors"));
            var ok = ((string?)null).Ok<string?, string>();
            Assert.That(ok.IsSuccess, Is.True);
            Assert.That(ok.Value, Is.Null);
            Assert.That(((string?)null).Ok().TryGetValue(out var value) && value is null, Is.True);
        }

        // Decision 10's default (DECISIONS-TAKEN, awaiting the maintainer on #39): the zero value is neither a success nor
        // a failure, and every member that reads the case throws; 08b extends this to the 0.2 members.
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
            Assert.That(() => zero.Match(v => v, _ => -1), Throws.InvalidOperationException.With.Message.Contains("zero value"));

            var zeroList = default(Result<int>);
            Assert.That(zeroList.IsSuccess, Is.False);
            Assert.That(zeroList.IsFailure, Is.False);
            Assert.That(() => zeroList.Value, Throws.InvalidOperationException.With.Message.Contains("zero value"));
            Assert.That(() => zeroList.TryGetValue(out _), Throws.InvalidOperationException);
            Assert.That(() => zeroList.OrElse(1.Ok()), Throws.InvalidOperationException);
            Assert.That(() => zeroList.ValueOr(1), Throws.InvalidOperationException);
            Assert.That(() => zeroList.Match(v => v, _ => -1), Throws.InvalidOperationException.With.Message.Contains("zero value"));
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
