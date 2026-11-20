using Xunit;

namespace Resultful.Consumption;

public sealed class ResultExtensionsSyncTests
{
    [Fact]
    public void Ok_Fail_Id_Flatten_Unfold_Unroll_and_UnrollUntil()
    {
        Assert.Equal(5, 5.Id());
        Assert.Equal(5, 5.Ok().Match(v => v, _ => -1));
        Assert.Equal(5, 5.Ok<int, TestError>().Match(v => v, _ => -1));
        Assert.True(Result.Ok().Match(_ => true, _ => false));
        Assert.True(Result.Ok<TestError>().Match(_ => true, _ => false));

        // Fail overloads
        Assert.Equal(["a", "b"], Result.Fail("a", "b").Match(_ => Array.Empty<string>(), e => e.ToArray()));
        Assert.Equal(["a"], ((IEnumerable<string>)["a"]).Fail().Match(_ => Array.Empty<string>(), e => e.ToArray()));
        Assert.Equal(["a", "b"], Result.Fail<int>("a", "b").Match(_ => Array.Empty<string>(), e => e.ToArray()));
        Assert.Equal(["a"], ((IEnumerable<string>)["a"]).Fail<int>().Match(_ => Array.Empty<string>(), e => e.ToArray()));
        Assert.Equal("e", new TestError("e").Fail<int, TestError>().Match(_ => "", e => e.Message));
        Assert.Equal(2, Result.Fail<int, TestError>(new TestError("a"), new TestError("b")).Match(_ => 0, e => e.Count()));
        Assert.Equal(1, ((IEnumerable<TestError>)[new TestError("a")]).Fail<int, TestError>().Match(_ => 0, e => e.Count()));
        Assert.Equal("e", new TestError("e").Fail().Match(_ => "", e => e.Message));
        Assert.Equal(2, Result.Fail(new TestError("a"), new TestError("b")).Match(_ => 0, e => e.Count()));

        // Flatten
        Assert.True(Result.Ok().Ok().Flatten().Match(_ => true, _ => false));
        Assert.Equal(5, 5.Ok().Ok().Flatten().Match(v => v, _ => -1));
        Assert.Equal(5, 5.Ok<int, TestError>().Ok<Result<int, TestError>, TestError>().Flatten().Match(v => v, _ => -1));
        Assert.True(Result.Ok<TestError>().Ok<VoidResult<TestError>, TestError>().Flatten().Match(_ => true, _ => false));
        Assert.True(Result.Ok<VoidResult<TestError>>().Flatten().Match(_ => true, _ => false));

        // Unfold
        Assert.Equal(5, 5.Some().Ok().Unfold().Or(0.Ok()).Match(v => v, _ => -1));
        Assert.Equal(5, 5.Some().Ok<Option<int>, TestError>().Unfold().Or(0.Ok<int, TestError>()).Match(v => v, _ => -1));

        // Unroll / UnrollUntil
        IEnumerable<Result<int>> okList = [1.Ok(), 2.Ok()];
        IEnumerable<Result<int, TestError>> okGenList = [1.Ok<int, TestError>(), 2.Ok<int, TestError>()];
        Assert.Equal([1, 2], okList.Unroll().Match(v => v.ToArray(), _ => []));
        Assert.Equal([1, 2], okGenList.Unroll((a, b) => a.Plus(b)).Match(v => v.ToArray(), _ => []));
        Assert.Equal([1, 2], okList.UnrollUntil().Match(v => v.ToArray(), _ => []));
        Assert.Equal([1, 2], okGenList.UnrollUntil().Match(v => v.ToArray(), _ => []));
    }

    [Fact]
    public void Plus_tuple_and_combiner_overloads_for_all_arities()
    {
        var r0 = 0.Ok();
        var r1 = 1.Ok();
        var r2 = 2.Ok();
        var r3 = 3.Ok();
        var r4 = 4.Ok();
        var r5 = 5.Ok();
        var r6 = 6.Ok();
        var r7 = 7.Ok();

        var g0 = 0.Ok<int, TestError>();
        var g1 = 1.Ok<int, TestError>();
        var g2 = 2.Ok<int, TestError>();
        var g3 = 3.Ok<int, TestError>();
        var g4 = 4.Ok<int, TestError>();
        var g5 = 5.Ok<int, TestError>();
        var g6 = 6.Ok<int, TestError>();
        var g7 = 7.Ok<int, TestError>();
        static TestError Merge(TestError a, TestError b) => a.Plus(b);

        // VoidResult Plus
        Assert.True(Result.Ok().Plus(Result.Ok()).Match(_ => true, _ => false));
        Assert.True(Result.Ok<TestError>().Plus(Result.Ok<TestError>()).Match(_ => true, _ => false));
        Assert.True(Result.Ok<TestError>().Plus(Result.Ok<TestError>(), Merge).Match(_ => true, _ => false));

        // 2-arity Plus (tuple, plusFunc, IPlus)
        Assert.Equal((0, 1), r0.Plus(r1).Match(v => v, _ => default));
        Assert.Equal(1, r0.Plus(r1, (a, b) => a + b).Match(v => v, _ => -1));
        Assert.Equal(new AddableInt(5), new AddableInt(2).Ok().Plus<AddableInt, AddableInt, AddableInt>(new AddableInt(3).Ok()).Match(v => v, _ => default));

        Assert.Equal((0, 1), g0.Plus(g1).Match(v => v, _ => default));
        Assert.Equal((0, 1), g0.Plus(g1, Merge).Match(v => v, _ => default));
        Assert.Equal(1, g0.Plus(g1, (a, b) => a + b).Match(v => v, _ => -1));
        Assert.Equal(1, g0.Plus(g1, Merge, (a, b) => a + b).Match(v => v, _ => -1));
        Assert.Equal(new AddableInt(5), new AddableInt(2).Ok<AddableInt, TestError>().Plus<AddableInt, AddableInt, AddableInt, TestError>(new AddableInt(3).Ok<AddableInt, TestError>()).Match(v => v, _ => default));
        Assert.Equal(new AddableInt(5), new AddableInt(2).Ok<AddableInt, TestError>().Plus<AddableInt, AddableInt, AddableInt, TestError>(new AddableInt(3).Ok<AddableInt, TestError>(), Merge).Match(v => v, _ => default));

        // 3..8-arity Plus (Result<T>, Result<T, TError> IPlus, Result<T, TError> mergeFunc)
        Assert.Equal((0, 1, 2), r0.Plus(r1, r2).Match(v => v, _ => default));
        Assert.Equal((0, 1, 2), g0.Plus(g1, g2).Match(v => v, _ => default));
        Assert.Equal((0, 1, 2), g0.Plus(g1, g2, Merge).Match(v => v, _ => default));

        Assert.Equal((0, 1, 2, 3), r0.Plus(r1, r2, r3).Match(v => v, _ => default));
        Assert.Equal((0, 1, 2, 3), g0.Plus(g1, g2, g3).Match(v => v, _ => default));
        Assert.Equal((0, 1, 2, 3), g0.Plus(g1, g2, g3, Merge).Match(v => v, _ => default));

        Assert.Equal((0, 1, 2, 3, 4), r0.Plus(r1, r2, r3, r4).Match(v => v, _ => default));
        Assert.Equal((0, 1, 2, 3, 4), g0.Plus(g1, g2, g3, g4).Match(v => v, _ => default));
        Assert.Equal((0, 1, 2, 3, 4), g0.Plus(g1, g2, g3, g4, Merge).Match(v => v, _ => default));

        Assert.Equal((0, 1, 2, 3, 4, 5), r0.Plus(r1, r2, r3, r4, r5).Match(v => v, _ => default));
        Assert.Equal((0, 1, 2, 3, 4, 5), g0.Plus(g1, g2, g3, g4, g5).Match(v => v, _ => default));
        Assert.Equal((0, 1, 2, 3, 4, 5), g0.Plus(g1, g2, g3, g4, g5, Merge).Match(v => v, _ => default));

        Assert.Equal((0, 1, 2, 3, 4, 5, 6), r0.Plus(r1, r2, r3, r4, r5, r6).Match(v => v, _ => default));
        Assert.Equal((0, 1, 2, 3, 4, 5, 6), g0.Plus(g1, g2, g3, g4, g5, g6).Match(v => v, _ => default));
        Assert.Equal((0, 1, 2, 3, 4, 5, 6), g0.Plus(g1, g2, g3, g4, g5, g6, Merge).Match(v => v, _ => default));

        Assert.Equal((0, 1, 2, 3, 4, 5, 6, 7), r0.Plus(r1, r2, r3, r4, r5, r6, r7).Match(v => v, _ => default));
        Assert.Equal((0, 1, 2, 3, 4, 5, 6, 7), g0.Plus(g1, g2, g3, g4, g5, g6, g7).Match(v => v, _ => default));
        Assert.Equal((0, 1, 2, 3, 4, 5, 6, 7), g0.Plus(g1, g2, g3, g4, g5, g6, g7, Merge).Match(v => v, _ => default));
    }

    [Fact]
    public void Fold_FoldUntil_Reduce_ReduceUntil_Check_Then_Sequence()
    {
        IEnumerable<Result<int>> nums = [1.Ok(), 2.Ok(), 3.Ok()];
        IEnumerable<Result<int, TestError>> gNums = [1.Ok<int, TestError>(), 2.Ok<int, TestError>(), 3.Ok<int, TestError>()];
        IEnumerable<Result<AddableInt>> addables = [new AddableInt(1).Ok(), new AddableInt(2).Ok()];
        IEnumerable<Result<AddableInt, TestError>> gAddables = [new AddableInt(1).Ok<AddableInt, TestError>(), new AddableInt(2).Ok<AddableInt, TestError>()];
        static TestError Merge(TestError a, TestError b) => a.Plus(b);

        // Fold / FoldUntil
        Assert.Equal(6, nums.Fold(0, (a, b) => a + b).Match(v => v, _ => -1));
        Assert.Equal(6, nums.Fold(0.Ok(), (a, b) => a + b).Match(v => v, _ => -1));
        Assert.Equal(6, gNums.Fold(0, (a, b) => a + b).Match(v => v, _ => -1));
        Assert.Equal(6, gNums.Fold(0.Ok<int, TestError>(), (a, b) => a + b).Match(v => v, _ => -1));
        Assert.Equal(6, gNums.Fold(0, Merge, (a, b) => a + b).Match(v => v, _ => -1));
        Assert.Equal(6, gNums.Fold(0.Ok<int, TestError>(), Merge, (a, b) => a + b).Match(v => v, _ => -1));

        Assert.Equal(6, nums.FoldUntil(0, (a, b) => a + b).Match(v => v, _ => -1));
        Assert.Equal(6, nums.FoldUntil(0.Ok(), (a, b) => a + b).Match(v => v, _ => -1));
        Assert.Equal(6, gNums.FoldUntil(0, (a, b) => a + b).Match(v => v, _ => -1));
        Assert.Equal(6, gNums.FoldUntil(0.Ok<int, TestError>(), (a, b) => a + b).Match(v => v, _ => -1));

        // Reduce / ReduceUntil
        Assert.Equal(6, nums.Reduce((a, b) => a + b).Match(v => v, _ => -1));
        Assert.Equal(new AddableInt(3), addables.Reduce().Match(v => v, _ => default));
        Assert.Equal(6, gNums.Reduce((a, b) => a + b).Match(v => v, _ => -1));
        Assert.Equal(6, gNums.Reduce(Merge, (a, b) => a + b).Match(v => v, _ => -1));
        Assert.Equal(new AddableInt(3), gAddables.Reduce().Match(v => v, _ => default));
        Assert.Equal(new AddableInt(3), gAddables.Reduce(Merge).Match(v => v, _ => default));

        Assert.True(new[] { Result.Ok(), Result.Ok() }.Reduce().Match(_ => true, _ => false));
        Assert.True(new[] { Result.Ok<TestError>(), Result.Ok<TestError>() }.Reduce().Match(_ => true, _ => false));
        Assert.True(new[] { Result.Ok<TestError>(), Result.Ok<TestError>() }.Reduce(Merge).Match(_ => true, _ => false));
        Assert.True(Result.Reduce(Result.Ok<TestError>(), Result.Ok<TestError>()).Match(_ => true, _ => false));

        Assert.Equal(6, nums.ReduceUntil((a, b) => a + b).Match(v => v, _ => -1));
        Assert.Equal(6, gNums.ReduceUntil((a, b) => a + b).Match(v => v, _ => -1));
        Assert.Equal(new AddableInt(3), gAddables.ReduceUntil().Match(v => v, _ => default));
        Assert.True(new[] { Result.Ok(), Result.Ok() }.ReduceUntil().Match(_ => true, _ => false));
        Assert.True(new[] { Result.Ok<TestError>(), Result.Ok<TestError>() }.ReduceUntil().Match(_ => true, _ => false));
        Assert.True(Result.ReduceUntil(Result.Ok<TestError>(), Result.Ok<TestError>()).Match(_ => true, _ => false));

        // Check / Then / Sequence
        Assert.Equal(10, 10.Ok().Check(x => x > 0 ? Result.Ok() : Result.Fail("neg")).Match(v => v, _ => -1));
        Assert.Equal(10, 10.Ok<int, TestError>().Check(x => x > 0 ? Result.Ok<TestError>() : new TestError("neg").Fail()).Match(v => v, _ => -1));

        Assert.Equal(12, 10.Ok().Then(x => (x + 1).Ok(), x => (x + 1).Ok()).Match(v => v, _ => -1));
        Assert.Equal(12, 10.Ok<int, TestError>().Then(x => (x + 1).Ok<int, TestError>(), x => (x + 1).Ok<int, TestError>()).Match(v => v, _ => -1));
        Assert.True(Result.Ok().Then(() => Result.Ok()).Match(_ => true, _ => false));
        Assert.True(Result.Ok<TestError>().Then(() => Result.Ok<TestError>()).Match(_ => true, _ => false));
        Assert.Equal(5, Result.Ok().Then(() => 5.Ok()).Match(v => v, _ => -1));
        Assert.Equal(5, Result.Ok<TestError>().Then(() => 5.Ok<int, TestError>()).Match(v => v, _ => -1));

        Assert.True(Result.Sequence(() => Result.Ok(), () => Result.Ok()).Match(_ => true, _ => false));
        Assert.True(Result.Sequence(() => Result.Ok<TestError>(), () => Result.Ok<TestError>()).Match(_ => true, _ => false));

        // Direct IPlus.Plus call
        Assert.Equal("a; b", ((IPlus<TestError, TestError>)new TestError("a")).Plus(new TestError("b")).Message);
    }

    // Failure paths and mixed inputs. These pin what 0.2 does today, including the parts 0.3 changes.

    [Fact]
    public void Plus_keeps_errors_left_then_right_and_never_calls_plusFunc_on_failure()
    {
        var plusCalls = 0;
        int Add(int a, int b)
        {
            plusCalls++;
            return a + b;
        }

        // Result<T>: errors concatenate, left first.
        Assert.Equal(["a"], Result.Fail<int>("a").Plus(1.Ok(), Add).Errors());
        Assert.Equal(["b"], 1.Ok().Plus(Result.Fail<int>("b"), Add).Errors());
        Assert.Equal(["a", "b", "c"], Result.Fail<int>("a", "b").Plus(Result.Fail<int>("c"), Add).Errors());
        Assert.Equal(["a", "c"], Result.Fail<int>("a").Plus(Result.Fail<int>("c")).Errors());
        Assert.Equal(0, plusCalls);

        // Result<T, TError>: mergeFunc runs only when both sides fail, and gets (left, right).
        var mergeCalls = 0;
        TestError Merge(TestError left, TestError right)
        {
            mergeCalls++;
            return new TestError($"{left.Message}+{right.Message}");
        }

        var okG = 1.Ok<int, TestError>();
        var failA = new TestError("a").Fail<int, TestError>();
        var failB = new TestError("b").Fail<int, TestError>();
        Assert.Equal("a", failA.Plus(okG, Merge, Add).Error());
        Assert.Equal("b", okG.Plus(failB, Merge, Add).Error());
        Assert.Equal(0, mergeCalls);
        Assert.Equal("a+b", failA.Plus(failB, Merge, Add).Error());
        Assert.Equal(1, mergeCalls);
        Assert.Equal("a; b", failA.Plus(failB).Error());
        Assert.Equal("a; b", failA.Plus(failB, Add).Error());
        Assert.Equal(0, plusCalls);

        // Tuple arities combine every failure, left to right.
        var ok = 0.Ok();
        Assert.Equal(["x", "y"], ok.Plus(Result.Fail<int>("x"), Result.Fail<int>("y")).Errors());
        Assert.Equal(["x", "z"], Result.Fail<int>("x").Plus(ok, ok, Result.Fail<int>("z")).Errors());
        Assert.Equal(
            ["1", "3", "5", "7"],
            Result.Fail<int>("1").Plus(ok, Result.Fail<int>("3"), ok, Result.Fail<int>("5"), ok, Result.Fail<int>("7"), ok).Errors());
        Assert.Equal("a; b", failA.Plus(okG, failB).Error());
        Assert.Equal("a; b", failA.Plus(okG, okG, failB).Error());
        Assert.Equal("a; b", failA.Plus(okG, okG, okG, okG, okG, okG, failB).Error());

        // The 3-arity merge nests to the right, the 4-arity one pairs up.
        static TestError Group(TestError left, TestError right) => new($"({left.Message}+{right.Message})");
        var failC = new TestError("c").Fail<int, TestError>();
        var failD = new TestError("d").Fail<int, TestError>();
        Assert.Equal("(a+(b+c))", failA.Plus(failB, failC, Group).Error());
        Assert.Equal("((a+b)+(c+d))", failA.Plus(failB, failC, failD, Group).Error());

        // VoidResult and VoidResult<TError>.
        Assert.Equal(["a", "b"], Result.Fail("a").Plus(Result.Fail("b")).Errors());
        Assert.Equal(["a"], Result.Fail("a").Plus(Result.Ok()).Errors());
        Assert.Equal(["b"], Result.Ok().Plus(Result.Fail("b")).Errors());
        Assert.Equal("a; b", new TestError("a").Fail().Plus(new TestError("b").Fail()).Error());
        Assert.Equal("a+b", new TestError("a").Fail().Plus(new TestError("b").Fail(), Merge).Error());
        Assert.Equal("a", new TestError("a").Fail().Plus(Result.Ok<TestError>(), Merge).Error());
    }

    [Fact]
    public void Fold_Reduce_and_Unroll_collect_every_error_in_order_and_stop_aggregating()
    {
        var aggrCalls = 0;
        int Add(int a, int b)
        {
            aggrCalls++;
            return a + b;
        }

        IEnumerable<Result<int>> mixed = [1.Ok(), Result.Fail<int>("a"), 3.Ok(), Result.Fail<int>("b", "c")];
        Assert.Equal(["a", "b", "c"], mixed.Fold(0, Add).Errors());
        Assert.Equal(1, aggrCalls);
        Assert.Equal(["seed", "a", "b", "c"], mixed.Fold(Result.Fail<int>("seed"), Add).Errors());
        Assert.Equal(["a", "b", "c"], mixed.Reduce(Add).Errors());
        Assert.Equal(["a", "b", "c"], mixed.Unroll().Errors());

        // An empty input folds to the seed but can't be reduced.
        Assert.Equal(7, Array.Empty<Result<int>>().Fold(7, Add).Match(v => v, _ => -1));
        Assert.Throws<ArgumentException>(() => Array.Empty<Result<int>>().Reduce(Add));
        Assert.Throws<ArgumentException>(() => Array.Empty<Result<int, TestError>>().Reduce(Add));
        Assert.Equal([], Array.Empty<Result<int>>().Unroll().Match(v => v.ToArray(), _ => [-1]));

        IEnumerable<Result<int, TestError>> gMixed =
            [1.Ok<int, TestError>(), new TestError("a").Fail<int, TestError>(), 3.Ok<int, TestError>(), new TestError("b").Fail<int, TestError>()];
        static TestError Merge(TestError left, TestError right) => new($"{left.Message}+{right.Message}");
        Assert.Equal("a; b", gMixed.Fold(0, Add).Error());
        Assert.Equal("a+b", gMixed.Fold(0, Merge, Add).Error());
        Assert.Equal("s; a; b", gMixed.Fold(new TestError("s").Fail<int, TestError>(), Add).Error());
        Assert.Equal("a; b", gMixed.Reduce(Add).Error());
        Assert.Equal("a+b", gMixed.Reduce(Merge, Add).Error());
        Assert.Equal("a+b", gMixed.Unroll(Merge).Match(_ => "", e => e.Message));

        IEnumerable<Result<AddableInt, TestError>> gAddables =
            [new AddableInt(1).Ok<AddableInt, TestError>(), new TestError("a").Fail<AddableInt, TestError>(), new TestError("b").Fail<AddableInt, TestError>()];
        Assert.Equal("a; b", gAddables.Reduce().Error());
        Assert.Equal("a+b", gAddables.Reduce(Merge).Error());
        IEnumerable<Result<AddableInt>> addables = [new AddableInt(1).Ok(), Result.Fail<AddableInt>("a"), Result.Fail<AddableInt>("b")];
        Assert.Equal(["a", "b"], addables.Reduce().Errors());

        // VoidResult Reduce collects; ReduceUntil keeps the first failure.
        VoidResult[] voids = [Result.Ok(), Result.Fail("a"), Result.Fail("b")];
        Assert.Equal(["a", "b"], voids.Reduce().Errors());
        Assert.Equal(["a"], voids.ReduceUntil().Errors());
        Assert.Equal([Outcome.Succeeded], Array.Empty<VoidResult>().Reduce().Errors());
        Assert.Equal([Outcome.Succeeded], Array.Empty<VoidResult>().ReduceUntil().Errors());

        VoidResult<TestError>[] gVoids = [Result.Ok<TestError>(), new TestError("a").Fail(), new TestError("b").Fail()];
        Assert.Equal("a; b", gVoids.Reduce().Error());
        Assert.Equal("a+b", gVoids.Reduce(Merge).Error());
        Assert.Equal("a; b", Result.Reduce(gVoids).Error());
        Assert.Equal("a", gVoids.ReduceUntil().Error());
        Assert.Equal("a", Result.ReduceUntil(gVoids).Error());
    }

    [Fact]
    public void Until_variants_keep_the_first_failure_and_stop_pulling_the_source()
    {
        var aggrCalls = 0;
        int Add(int a, int b)
        {
            aggrCalls++;
            return a + b;
        }

        Result<int>[] items = [1.Ok(), Result.Fail<int>("a"), Result.Fail<int>("b"), 4.Ok(), 5.Ok()];
        Result<int, TestError>[] gItems =
            [1.Ok<int, TestError>(), new TestError("a").Fail<int, TestError>(), new TestError("b").Fail<int, TestError>(), 4.Ok<int, TestError>(), 5.Ok<int, TestError>()];

        // Today the source is pulled once past the first failure, then enumeration stops.
        var pulled = 0;
        Assert.Equal(["a"], Outcome.Counted(items, () => pulled++).FoldUntil(0, Add).Errors());
        Assert.Equal(3, pulled);
        Assert.Equal(1, aggrCalls);

        pulled = 0;
        Assert.Equal(["a"], Outcome.Counted(items, () => pulled++).ReduceUntil(Add).Errors());
        Assert.Equal(3, pulled);

        pulled = 0;
        Assert.Equal(["a"], Outcome.Counted(items, () => pulled++).UnrollUntil().Errors());
        Assert.Equal(3, pulled);

        pulled = 0;
        Assert.Equal("a", Outcome.Counted(gItems, () => pulled++).FoldUntil(0, Add).Error());
        Assert.Equal(3, pulled);

        pulled = 0;
        Assert.Equal("a", Outcome.Counted(gItems, () => pulled++).ReduceUntil(Add).Error());
        Assert.Equal(3, pulled);

        pulled = 0;
        Assert.Equal("a", Outcome.Counted(gItems, () => pulled++).UnrollUntil().Error());
        Assert.Equal(3, pulled);

        // A failed seed stops at once.
        pulled = 0;
        Assert.Equal(["seed"], Outcome.Counted(items, () => pulled++).FoldUntil(Result.Fail<int>("seed"), Add).Errors());
        Assert.Equal(1, pulled);

        // The non-Until variants read everything.
        pulled = 0;
        Assert.Equal(["a", "b"], Outcome.Counted(items, () => pulled++).Fold(0, Add).Errors());
        Assert.Equal(items.Length, pulled);

        // Each pass called the aggregator once, for 1 + nothing, before the first failure.
        Assert.Equal(3, aggrCalls);
        Assert.Throws<ArgumentException>(() => Array.Empty<Result<int>>().ReduceUntil(Add));
    }

    [Fact]
    public void Then_Check_and_Sequence_return_the_first_failure_and_skip_later_steps()
    {
        var later = 0;
        VoidResult LaterVoid()
        {
            later++;
            return Result.Fail("later");
        }

        VoidResult<TestError> LaterGVoid()
        {
            later++;
            return new TestError("later").Fail();
        }

        Assert.Equal(["a"], Result.Sequence(Result.Ok, () => Result.Fail("a"), LaterVoid).Errors());
        Assert.Equal("a", Result.Sequence(Result.Ok<TestError>, () => new TestError("a").Fail(), LaterGVoid).Error());
        Assert.Equal(["pre"], Result.Fail("pre").Then(LaterVoid).Errors());
        Assert.Equal("pre", new TestError("pre").Fail().Then(LaterGVoid).Error());
        Assert.Equal(["pre"], Result.Fail("pre").Then(() => { later++; return 1.Ok(); }).Errors());
        Assert.Equal("pre", new TestError("pre").Fail().Then(() => { later++; return 1.Ok<int, TestError>(); }).Error());

        Assert.Equal(["a"], 10.Ok().Then(_ => Result.Fail<int>("a"), x => { later++; return x.Ok(); }).Errors());
        Assert.Equal("a", 10.Ok<int, TestError>().Then(_ => new TestError("a").Fail<int, TestError>(), x => { later++; return x.Ok<int, TestError>(); }).Error());
        Assert.Equal(["pre"], Result.Fail<int>("pre").Then(x => { later++; return x.Ok(); }).Errors());

        Assert.Equal(["c1"], 10.Ok().Check(_ => Result.Fail("c1"), _ => LaterVoid()).Errors());
        Assert.Equal("c1", 10.Ok<int, TestError>().Check(_ => new TestError("c1").Fail(), _ => LaterGVoid()).Error());
        Assert.Equal(["pre"], Result.Fail<int>("pre").Check(_ => LaterVoid()).Errors());
        Assert.Equal(0, later);

        // A step can fail on a value that passed the step before it.
        Assert.Equal(["11 is odd"], 10.Ok().Then(x => (x + 1).Ok(), x => x % 2 == 0 ? x.Ok() : Result.Fail<int>($"{x} is odd")).Errors());

        // Every step is checked for null before anything runs, even on a failed input.
        Assert.Throws<ArgumentNullException>(() => Result.Fail("pre").Then(Result.Ok, null!));
        Assert.Throws<ArgumentNullException>(() => Result.Fail<int>("pre").Check(null!));
    }

    [Fact]
    public void Flatten_Unfold_and_Fail_on_failure_and_edge_inputs()
    {
        Assert.Equal(["outer"], Result.Fail<Result<int>>("outer").Flatten().Errors());
        Assert.Equal(["inner"], Result.Fail<int>("inner").Ok().Flatten().Errors());
        Assert.Equal(["outer"], Result.Fail<VoidResult>("outer").Flatten().Errors());
        Assert.Equal(["inner"], Result.Fail("inner").Ok().Flatten().Errors());
        Assert.Equal("outer", new TestError("outer").Fail<Result<int, TestError>, TestError>().Flatten().Error());
        Assert.Equal("inner", new TestError("inner").Fail<int, TestError>().Ok<Result<int, TestError>, TestError>().Flatten().Error());
        Assert.Equal("inner", new TestError("inner").Fail().Ok<VoidResult<TestError>, TestError>().Flatten().Error());

        // Unfold: an error becomes Some(failure); a None value becomes None.
        Assert.Equal(["e"], Result.Fail<Option<int>>("e").Unfold().Map(r => r.Errors()).Or([]));
        Assert.False(Option<int>.None.Ok().Unfold().Map(_ => true).Or(false));
        Assert.Equal("e", new TestError("e").Fail<Option<int>, TestError>().Unfold().Map(r => r.Error()).Or(""));
        Assert.False(Option<int>.None.Ok<Option<int>, TestError>().Unfold().Map(_ => true).Or(false));

        // Fail with no errors is still a failure, with an empty error list.
        Assert.Equal([], Result.Fail<int>(Array.Empty<string>()).Errors());
        Assert.Equal([], Result.Fail().Errors());
        Assert.Equal([], Result.Fail<int, TestError>().Match(_ => [new TestError("ok")], e => e.ToArray()));
    }
}
