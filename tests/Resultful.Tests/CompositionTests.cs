using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace Resultful.Tests
{
    [TestFixture]
    public class CompositionTests
    {
        private List<string> _calls = new();

        [SetUp]
        public void SetUp() => _calls = new List<string>();

        private sealed record Error(string Message);

        // Steps used as method groups (the shape the README recommends).
        private VoidResult Validate() { _calls.Add(nameof(Validate)); return Result.Ok(); }
        private VoidResult Reserve() { _calls.Add(nameof(Reserve)); return Result.Ok(); }
        private VoidResult OutOfStock() { _calls.Add(nameof(OutOfStock)); return Result.Fail("out of stock", "warehouse closed"); }
        private VoidResult Notify() { _calls.Add(nameof(Notify)); return Result.Ok(); }

        private Result<string> Trim(string s) { _calls.Add(nameof(Trim)); return s.Trim().Ok(); }
        private Result<string> NotEmpty(string s) { _calls.Add(nameof(NotEmpty)); return s.Length == 0 ? "empty" : s.Ok(); }
        private Result<string> Upper(string s) { _calls.Add(nameof(Upper)); return s.ToUpperInvariant().Ok(); }

        private static IEnumerable<string> Errors<T>(Result<T> r) => r.Match(_ => throw new AssertionException("expected failure"), e => e);
        private static IEnumerable<string> Errors(VoidResult r) => r.Match(_ => throw new AssertionException("expected failure"), e => e);
        private static T Value<T>(Result<T> r) => r.Match(v => v, e => throw new AssertionException(string.Join(", ", e)));

        // ---- Sequence / Then on VoidResult ----

        [Test]
        public void Sequence_RunsEveryStepInOrder_WhenAllSucceed()
        {
            var result = Result.Sequence(Validate, Reserve, Notify);

            result.Match(_ => true, _ => false).Should().BeTrue();
            _calls.Should().Equal(nameof(Validate), nameof(Reserve), nameof(Notify));
        }

        [Test]
        public void Sequence_StopsAtFirstFailure_AndKeepsItsAccumulatedErrors()
        {
            var result = Result.Sequence(Validate, OutOfStock, Notify);

            Errors(result).Should().Equal("out of stock", "warehouse closed");
            _calls.Should().Equal(nameof(Validate), nameof(OutOfStock));
        }

        [Test]
        public void Then_OnFailedVoidResult_RunsNothing()
        {
            var result = Result.Fail("already failed").Then(Validate, Notify);

            Errors(result).Should().Equal("already failed");
            _calls.Should().BeEmpty();
        }

        [Test]
        public void Then_AcceptsLambdasUsingImplicitStringConversion()
        {
            var result = Result.Ok().Then(() => Result.Ok(), () => "boom", Notify);

            Errors(result).Should().Equal("boom");
            _calls.Should().BeEmpty();
        }

        [Test]
        public void Then_FromVoidResultToValue_DoesNotNeedUnitArgument()
        {
            var result = Result.Sequence(Validate, Reserve).Then(() => "  order-1 ".Ok()).Then(Trim, Upper);

            Value(result).Should().Be("ORDER-1");
            _calls.Should().Equal(nameof(Validate), nameof(Reserve), nameof(Trim), nameof(Upper));
        }

        [Test]
        public void Then_FromFailedVoidResultToValue_SkipsNext()
        {
            var invoked = false;
            var result = Result.Sequence(OutOfStock).Then(() => { invoked = true; return 1.Ok(); });

            Errors(result).Should().Equal("out of stock", "warehouse closed");
            invoked.Should().BeFalse();
        }

        // ---- Then / Check on Result<T> ----

        [Test]
        public void Then_OnResult_PipesValueThroughSameTypedSteps()
        {
            var result = "  hello ".Ok().Then(Trim, NotEmpty, Upper);

            Value(result).Should().Be("HELLO");
            _calls.Should().Equal(nameof(Trim), nameof(NotEmpty), nameof(Upper));
        }

        [Test]
        public void Then_OnResult_ShortCircuits()
        {
            var result = "   ".Ok().Then(Trim, NotEmpty, Upper);

            Errors(result).Should().Equal("empty");
            _calls.Should().Equal(nameof(Trim), nameof(NotEmpty));
        }

        [Test]
        public void Check_KeepsValue_AndShortCircuits()
        {
            var ok = 5.Ok().Check(x => x > 0 ? Result.Ok() : "not positive", x => x < 10 ? Result.Ok() : "too big");
            Value(ok).Should().Be(5);

            var secondRan = false;
            var failed = (-1).Ok().Check(x => x > 0 ? Result.Ok() : "not positive", x => { secondRan = true; return Result.Ok(); });
            Errors(failed).Should().Equal("not positive");
            secondRan.Should().BeFalse();
        }

        // ---- Generic error type ----

        [Test]
        public void GenericError_SequenceThenCheck_ReturnsFirstError()
        {
            var steps = new List<int>();
            VoidResult<Error> Step(int n, bool ok) { steps.Add(n); return ok ? Result.Ok<Error>() : new Error($"step {n}"); }

            var result = Result.Sequence<Error>(() => Step(1, true), () => Step(2, false), () => Step(3, true))
                .Then(() => 10.Ok<int, Error>())
                .Then(x => (x * 2).Ok<int, Error>())
                .Check(x => Step(4, true));

            result.Match(_ => (Error?)null, e => e).Should().Be(new Error("step 2"));
            steps.Should().Equal(1, 2);
        }

        [Test]
        public void GenericError_ThenAndCheck_OnSuccess()
        {
            var result = 10.Ok<int, Error>()
                .Then(x => (x + 1).Ok<int, Error>(), x => x > 100 ? new Error("too big") : (x * 2).Ok<int, Error>())
                .Check(x => x % 2 == 0 ? Result.Ok<Error>() : new Error("odd"));

            result.Match(v => v, e => -1).Should().Be(22);
        }

        // ---- Async ----

        private async Task<VoidResult> StepAsync(string name, bool ok)
        {
            await Task.Yield();
            _calls.Add(name);
            return ok ? Result.Ok() : Result.Fail(name + " failed");
        }

        [Test]
        public async Task SequenceAsync_StopsAtFirstFailure()
        {
            var result = await Result.SequenceAsync(
                () => StepAsync("a", true),
                () => StepAsync("b", false),
                () => StepAsync("c", true));

            Errors(result).Should().Equal("b failed");
            _calls.Should().Equal("a", "b");
        }

        [Test]
        public async Task TaskReceiver_MixesSyncAndAsyncSteps()
        {
            var result = await Result.SequenceAsync(() => StepAsync("load", true))
                .Then(Validate)
                .ThenAsync(async () => { await Task.Yield(); return "  x ".Ok(); })
                .Then(Trim, Upper)
                .CheckAsync(async s => await StepAsync("audit:" + s, true))
                .Check(s => s.Length == 1 ? Result.Ok() : "length");

            Value(result).Should().Be("X");
            _calls.Should().Equal("load", nameof(Validate), nameof(Trim), nameof(Upper), "audit:X");
        }

        [Test]
        public async Task ThenAsync_OnResult_ShortCircuits()
        {
            var result = await 1.Ok().ThenAsync(
                async x => { await Task.Yield(); _calls.Add("inc"); return (x + 1).Ok(); },
                async x => { await Task.Yield(); _calls.Add("fail"); return Result.Fail<int>("nope", "still nope"); },
                async x => { await Task.Yield(); _calls.Add("never"); return x.Ok(); });

            Errors(result).Should().Equal("nope", "still nope");
            _calls.Should().Equal("inc", "fail");
        }

        [Test]
        public async Task GenericError_Async_ShortCircuits()
        {
            var ran = new List<int>();
            async Task<VoidResult<Error>> Step(int n, bool ok) { await Task.Yield(); ran.Add(n); return ok ? Result.Ok<Error>() : new Error($"step {n}"); }

            var result = await Result.SequenceAsync<Error>(() => Step(1, true), () => Step(2, false), () => Step(3, true))
                .ThenAsync(async () => { await Task.Yield(); return 1.Ok<int, Error>(); });

            result.Match(_ => (Error?)null, e => e).Should().Be(new Error("step 2"));
            ran.Should().Equal(1, 2);
        }

        // ---- Guards ----

        [Test]
        public void NullSteps_Throw()
        {
            FluentActions.Invoking(() => Result.Sequence(Validate, null!)).Should().Throw<ArgumentNullException>();
            FluentActions.Invoking(() => Result.Sequence((Func<VoidResult>[])null!)).Should().Throw<ArgumentNullException>();
            FluentActions.Invoking(() => 1.Ok().Check(null!)).Should().Throw<ArgumentNullException>().WithParameterName("checks");
            FluentActions.Awaiting(() => Result.SequenceAsync(null!)).Should().ThrowAsync<ArgumentNullException>();
        }

        [Test]
        public void EmptySequence_IsSuccess()
        {
            Result.Sequence().Match(_ => true, _ => false).Should().BeTrue();
            "a".Ok().Then().Match(v => v, _ => "").Should().Be("a");
        }
    }
}
