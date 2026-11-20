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
        IPlus<TestError, TestError> plusRef = new TestError("a");
        Assert.Equal("a; b", plusRef.Plus(new TestError("b")).Message);
    }
}
