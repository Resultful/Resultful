using CsCheck;
using Xunit;

namespace Resultful.Consumption;

/// <summary>
/// Property-based law tests using CsCheck (Decision 6):
/// functor/monad laws, short-circuiting, error accumulation, and sync/async equivalence.
/// </summary>
public sealed class PropertyLawTests
{
    private static readonly Gen<Result<int, TestError>> GenGenericResult =
        Gen.OneOf(
            Gen.Int.Select(i => i.Ok<int, TestError>()),
            Gen.String[1, 16].Select(s => new TestError(s).Fail<int, TestError>()));

    private static readonly Gen<Result<int>> GenErrorListResult =
        Gen.OneOf(
            Gen.Int.Select(i => i.Ok()),
            Gen.String[1, 16].Array[1, 3].Select(errs => Result.Fail<int>(errs)));

    private static readonly Gen<Option<int>> GenOption =
        Gen.OneOf(
            Gen.Int.Select(i => i.Some()),
            Gen.Const(Option<int>.None));

    [Fact]
    public void Functor_laws_hold_for_Result_and_Option()
    {
        GenGenericResult.Sample(r =>
        {
            // Identity: Map(id) == r
            AssertEqual(r, r.Map(x => x));
            // Composition: Map(f).Map(g) == Map(x => g(f(x)))
            static int F(int x) => unchecked(x * 3 + 1);
            static string G(int x) => $"#{x}";
            AssertEqual(r.Map(F).Map(G), r.Map(x => G(F(x))));
        });

        GenOption.Sample(o =>
        {
            Assert.Equal(o.Match(x => (true, x), _ => (false, 0)), o.Map(x => x).Match(x => (true, x), _ => (false, 0)));
            static int F(int x) => unchecked(x + 7);
            static string G(int x) => $"#{x}";
            Assert.Equal(
                o.Map(F).Map(G).Match(x => x, _ => "<none>"),
                o.Map(x => G(F(x))).Match(x => x, _ => "<none>"));
        });
    }

    [Fact]
    public void Monad_laws_hold_for_Result_generic_and_error_list()
    {
        static Result<int, TestError> F(int x) => x % 2 == 0 ? unchecked(x / 2).Ok<int, TestError>() : new TestError("odd").Fail<int, TestError>();
        static Result<string, TestError> G(int x) => x >= 0 ? x.ToString().Ok<string, TestError>() : new TestError("neg").Fail<string, TestError>();

        // Left identity: Ok(a).Bind(F) == F(a)
        Gen.Int.Sample(a => AssertEqual(a.Ok<int, TestError>().Bind(F), F(a)));

        // Right identity: r.Bind(Ok) == r
        GenGenericResult.Sample(r => AssertEqual(r.Bind(x => x.Ok<int, TestError>()), r));

        // Associativity: r.Bind(F).Bind(G) == r.Bind(x => F(x).Bind(G))
        GenGenericResult.Sample(r => AssertEqual(r.Bind(F).Bind(G), r.Bind(x => F(x).Bind(G))));

        // Same for Result<T>
        static Result<int> F2(int x) => x % 2 == 0 ? (x / 2).Ok() : Result.Fail<int>("odd");
        static Result<string> G2(int x) => x >= 0 ? x.ToString().Ok() : (Result<string>)"neg";

        GenErrorListResult.Sample(r =>
        {
            AssertEqual(r.Bind(x => x.Ok()), r);
            AssertEqual(r.Bind(F2).Bind(G2), r.Bind(x => F2(x).Bind(G2)));
        });
    }

    [Fact]
    public void Short_circuit_laws_for_Bind_Then_Sequence_and_Check()
    {
        Gen.String[1, 12].Sample(err =>
        {
            var invoked = false;
            var failed = new TestError(err).Fail<int, TestError>();

            var afterBind = failed.Bind(x => { invoked = true; return (x + 1).Ok<int, TestError>(); });
            var afterThen = failed.Then(x => { invoked = true; return (x + 1).Ok<int, TestError>(); });
            var afterCheck = failed.Check(_ => { invoked = true; return Result.Ok<TestError>(); });
            var afterSeq = Result.Sequence(
                () => new TestError(err).Fail(),
                () => { invoked = true; return Result.Ok<TestError>(); });

            Assert.False(invoked);
            Assert.Equal(err, afterBind.Match(_ => "", e => e.Message));
            Assert.Equal(err, afterThen.Match(_ => "", e => e.Message));
            Assert.Equal(err, afterCheck.Match(_ => "", e => e.Message));
            Assert.Equal(err, afterSeq.Match(_ => "", e => e.Message));
        });
    }

    [Fact]
    public void Accumulation_laws_for_Plus_and_Fold_preserve_all_errors_in_order()
    {
        Gen.Select(Gen.String[1, 8], Gen.String[1, 8], Gen.String[1, 8]).Sample((e1, e2, e3) =>
        {
            Result<int> r1 = e1;
            Result<int> r2 = e2;
            Result<int> r3 = e3;

            var leftGrouped = r1.Plus(r2, (a, b) => a + b).Plus(r3, (ab, c) => ab + c);
            var rightGrouped = r1.Plus(r2.Plus(r3, (b, c) => b + c), (a, bc) => a + bc);

            var leftErrs = leftGrouped.Match(_ => Array.Empty<string>(), e => e.ToArray());
            var rightErrs = rightGrouped.Match(_ => Array.Empty<string>(), e => e.ToArray());

            Assert.Equal([e1, e2, e3], leftErrs);
            Assert.Equal(leftErrs, rightErrs);

            var folded = new[] { r1, r2, r3 }.Fold(0, (a, b) => a + b);
            Assert.Equal([e1, e2, e3], folded.Match(_ => Array.Empty<string>(), e => e.ToArray()));
        });
    }

    [Fact]
    public void Sync_and_async_operations_are_equivalent()
    {
        GenGenericResult.Sample(r =>
        {
            var syncMap = r.Map(x => unchecked(x * 2));
            var asyncMap = r.MapAsync(x => Task.FromResult(unchecked(x * 2))).GetAwaiter().GetResult();
            AssertEqual(syncMap, asyncMap);

            var syncBind = r.Bind(x => x >= 0 ? (x + 1).Ok<int, TestError>() : new TestError("neg").Fail<int, TestError>());
            var asyncBind = r.BindAsync(x => Task.FromResult(x >= 0 ? (x + 1).Ok<int, TestError>() : new TestError("neg").Fail<int, TestError>())).GetAwaiter().GetResult();
            AssertEqual(syncBind, asyncBind);

            var syncMatch = r.Match(v => $"ok:{v}", e => $"err:{e.Message}");
            var asyncMatch = r.MatchAsync(v => Task.FromResult($"ok:{v}"), e => Task.FromResult($"err:{e.Message}")).GetAwaiter().GetResult();
            Assert.Equal(syncMatch, asyncMatch);
        });
    }

    private static void AssertEqual<T>(Result<T, TestError> a, Result<T, TestError> b)
    {
        var aRep = a.Match(v => (true, (object?)v, string.Empty), e => (false, (object?)null, e.Message));
        var bRep = b.Match(v => (true, (object?)v, string.Empty), e => (false, (object?)null, e.Message));
        Assert.Equal(aRep, bRep);
    }

    private static void AssertEqual<T>(Result<T> a, Result<T> b)
    {
        var aRep = a.Match(v => (true, (object?)v, string.Empty), e => (false, (object?)null, string.Join("|", e)));
        var bRep = b.Match(v => (true, (object?)v, string.Empty), e => (false, (object?)null, string.Join("|", e)));
        Assert.Equal(aRep, bRep);
    }
}
