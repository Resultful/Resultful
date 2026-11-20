using Xunit;

namespace Resultful.Consumption;

public sealed class ResultExtensionsAsyncTests
{
    [Fact]
    public async Task Task_extensions_for_Result_and_VoidResult()
    {
        var tOk = Task.FromResult(10.Ok());
        var tFail = Task.FromResult(Result.Fail<int>("bad"));
        var tGenOk = Task.FromResult(10.Ok<int, TestError>());
        var tGenFail = Task.FromResult(new TestError("bad").Fail<int, TestError>());
        var tVoidOk = Task.FromResult(Result.Ok());
        var tVoidFail = Task.FromResult(Result.Fail("bad"));
        var tGenVoidOk = Task.FromResult(Result.Ok<TestError>());
        var tGenVoidFail = Task.FromResult(new TestError("bad").Fail());

        Assert.Equal(5, await 5.IdAsync());

        // Bind / BindAsync / BindValue / BindValueAsync
        Assert.Equal(20, (await tOk.Bind(x => (x * 2).Ok())).Match(v => v, _ => -1));
        Assert.Equal(20, (await tGenOk.Bind(x => (x * 2).Ok<int, TestError>())).Match(v => v, _ => -1));
        Assert.True((await tVoidOk.Bind(_ => Result.Ok())).Match(_ => true, _ => false));
        Assert.True((await tGenVoidOk.Bind(_ => Result.Ok<TestError>())).Match(_ => true, _ => false));

        Assert.Equal(20, (await tOk.BindAsync(x => Task.FromResult((x * 2).Ok()))).Match(v => v, _ => -1));
        Assert.Equal(20, (await tGenOk.BindAsync(x => Task.FromResult((x * 2).Ok<int, TestError>()))).Match(v => v, _ => -1));
        Assert.True((await tVoidOk.BindAsync(_ => Task.FromResult(Result.Ok()))).Match(_ => true, _ => false));
        Assert.True((await tGenVoidOk.BindAsync(_ => Task.FromResult(Result.Ok<TestError>()))).Match(_ => true, _ => false));

        Assert.Equal(5, (await tVoidOk.BindValue(_ => 5.Ok())).Match(v => v, _ => -1));
        Assert.Equal(5, (await tGenVoidOk.BindValue(_ => 5.Ok<int, TestError>())).Match(v => v, _ => -1));
        Assert.Equal(5, (await tVoidOk.BindValueAsync(_ => Task.FromResult(5.Ok()))).Match(v => v, _ => -1));
        Assert.Equal(5, (await tGenVoidOk.BindValueAsync(_ => Task.FromResult(5.Ok<int, TestError>()))).Match(v => v, _ => -1));

        // Map / MapAsync / Map2 / Map2Async / MapError / MapErrorAsync
        Assert.Equal("10", (await tOk.Map(x => x.ToString())).Match(v => v, _ => ""));
        Assert.Equal("10", (await tGenOk.Map(x => x.ToString())).Match(v => v, _ => ""));
        Assert.Equal(7, (await tVoidOk.Map(7)).Match(v => v, _ => -1));
        Assert.Equal(7, (await tVoidOk.Map(_ => 7)).Match(v => v, _ => -1));
        Assert.Equal(7, (await tGenVoidOk.Map(7)).Match(v => v, _ => -1));
        Assert.Equal(7, (await tGenVoidOk.Map(_ => 7)).Match(v => v, _ => -1));

        Assert.Equal("10", (await tOk.MapAsync(x => Task.FromResult(x.ToString()))).Match(v => v, _ => ""));
        Assert.Equal("10", (await tGenOk.MapAsync(x => Task.FromResult(x.ToString()))).Match(v => v, _ => ""));
        Assert.Equal(7, (await tVoidOk.MapAsync(Task.FromResult(7))).Match(v => v, _ => -1));
        Assert.Equal(7, (await tVoidOk.MapAsync(_ => Task.FromResult(7))).Match(v => v, _ => -1));
        Assert.Equal(7, (await tGenVoidOk.MapAsync(Task.FromResult(7))).Match(v => v, _ => -1));
        Assert.Equal(7, (await tGenVoidOk.MapAsync(_ => Task.FromResult(7))).Match(v => v, _ => -1));

        Assert.Equal("10", (await tOk.Map2(e => string.Join(",", e), x => x.ToString())).Match(v => v, e => e));
        Assert.Equal("10", (await tGenOk.Map2(e => e.Message, x => x.ToString())).Match(v => v, e => e));
        Assert.Equal(7, (await tVoidOk.Map2(e => string.Join(",", e), _ => 7)).Match(v => v, _ => -1));
        Assert.Equal(7, (await tGenVoidOk.Map2(e => e.Message, _ => 7)).Match(v => v, _ => -1));

        Assert.Equal("10", (await tOk.Map2Async(e => Task.FromResult(string.Join(",", e)), x => Task.FromResult(x.ToString()))).Match(v => v, e => e));
        Assert.Equal("10", (await tGenOk.Map2Async(e => Task.FromResult(e.Message), x => Task.FromResult(x.ToString()))).Match(v => v, e => e));
        Assert.Equal(7, (await tVoidOk.Map2Async(e => Task.FromResult(string.Join(",", e)), () => Task.FromResult(7))).Match(v => v, _ => -1));
        Assert.Equal(7, (await tGenVoidOk.Map2Async(e => Task.FromResult(e.Message), () => Task.FromResult(7))).Match(v => v, _ => -1));

        Assert.Equal("bad", (await tFail.MapError(e => string.Join(",", e))).Match(_ => "", e => e));
        Assert.Equal("BAD", (await tGenFail.MapError(e => e.Message.ToUpperInvariant())).Match(_ => "", e => e));
        Assert.Equal(["bad"], (await tGenFail.MapError(e => (IEnumerable<string>)[e.Message])).Match(_ => Array.Empty<string>(), e => e.ToArray()));
        Assert.Equal(["BAD"], (await tVoidFail.MapError(e => e.Select(s => s.ToUpperInvariant()))).Match(_ => Array.Empty<string>(), e => e.ToArray()));
        Assert.Equal("bad", (await tVoidFail.MapError(e => string.Join(",", e))).Match(_ => "", e => e));
        Assert.Equal("BAD", (await tGenVoidFail.MapError(e => e.Message.ToUpperInvariant())).Match(_ => "", e => e));

        Assert.Equal("bad", (await tFail.MapErrorAsync(e => Task.FromResult(string.Join(",", e)))).Match(_ => "", e => e));
        Assert.Equal("BAD", (await tGenFail.MapErrorAsync(e => Task.FromResult(e.Message.ToUpperInvariant()))).Match(_ => "", e => e));
        Assert.Equal(["bad"], (await tGenFail.MapErrorAsync(e => Task.FromResult<IEnumerable<string>>([e.Message]))).Match(_ => Array.Empty<string>(), e => e.ToArray()));
        Assert.Equal(["BAD"], (await tVoidFail.MapErrorAsync(e => Task.FromResult(e.Select(s => s.ToUpperInvariant())))).Match(_ => Array.Empty<string>(), e => e.ToArray()));
        Assert.Equal("bad", (await tVoidFail.MapErrorAsync(e => Task.FromResult(string.Join(",", e)))).Match(_ => "", e => e));
        Assert.Equal("BAD", (await tGenVoidFail.MapErrorAsync(e => Task.FromResult(e.Message.ToUpperInvariant()))).Match(_ => "", e => e));

        // Match / MatchAsync / Switch / SwitchAsync / ToOneOf
        Assert.Equal(10, await tOk.Match(v => v, _ => -1));
        Assert.Equal(10, await tGenOk.Match(v => v, _ => -1));
        Assert.Equal(1, await tVoidOk.Match<Unit, int>(_ => 1, _ => 0));
        Assert.Equal(1, await tGenVoidOk.Match<Unit, TestError, int>(_ => 1, _ => 0));

        Assert.Equal(10, await tOk.MatchAsync(v => Task.FromResult(v), _ => Task.FromResult(-1)));
        Assert.Equal(10, await tGenOk.MatchAsync(v => Task.FromResult(v), _ => Task.FromResult(-1)));
        Assert.Equal(1, await tVoidOk.MatchAsync<Unit, int>(_ => Task.FromResult(1), _ => Task.FromResult(0)));
        Assert.Equal(1, await tGenVoidOk.MatchAsync<Unit, TestError, int>(_ => Task.FromResult(1), _ => Task.FromResult(0)));

        await tOk.Switch(_ => { }, _ => { });
        await tGenOk.Switch(_ => { }, _ => { });
        await tVoidOk.Switch<Unit>(_ => { }, _ => { });
        await tGenVoidOk.Switch<Unit, TestError>(_ => { }, _ => { });

        await tOk.SwitchAsync(_ => Task.CompletedTask, _ => Task.CompletedTask);
        await tGenOk.SwitchAsync(_ => Task.CompletedTask, _ => Task.CompletedTask);
        await tVoidOk.SwitchAsync<Unit>(_ => Task.CompletedTask, _ => Task.CompletedTask);
        await tGenVoidOk.SwitchAsync<Unit, TestError>(_ => Task.CompletedTask, _ => Task.CompletedTask);

        Assert.True((await tOk.ToOneOf()).IsT0);
        Assert.True((await tGenOk.ToOneOf()).IsT0);
        Assert.True((await tVoidOk.ToOneOf<Unit>()).IsT0);
        Assert.True((await tGenVoidOk.ToOneOf<Unit, TestError>()).IsT0);

        // Tee / TeeAsync / TeeError / TeeErrorAsync
        await tOk.Tee(_ => { });
        await tGenOk.Tee(_ => { });
        await tVoidOk.Tee(_ => { });
        await tGenVoidOk.Tee(_ => { });

        await tOk.TeeAsync(_ => Task.CompletedTask);
        await tGenOk.TeeAsync(_ => Task.CompletedTask);
        await tVoidOk.TeeAsync(_ => Task.CompletedTask);
        await tGenVoidOk.TeeAsync(_ => Task.CompletedTask);

        await tFail.TeeError(_ => { });
        await tGenFail.TeeError(_ => { });
        await tVoidFail.TeeError(_ => { });
        await tGenVoidFail.TeeError(_ => { });

        await tFail.TeeErrorAsync(_ => Task.CompletedTask);
        await tGenFail.TeeErrorAsync(_ => Task.CompletedTask);
        await tVoidFail.TeeErrorAsync(_ => Task.CompletedTask);
        await tGenVoidFail.TeeErrorAsync(_ => Task.CompletedTask);

        // DiscardError / DiscardErrorAsync / DiscardValue / DiscardValueAsync
        Assert.Equal(10, (await tOk.DiscardError()).Or(-1));
        Assert.Equal(-1, (await tFail.DiscardError(_ => { })).Or(-1));
        Assert.Equal(10, (await tGenOk.DiscardError()).Or(-1));
        Assert.Equal(-1, (await tGenFail.DiscardError(_ => { })).Or(-1));
        Assert.Equal(-1, (await tFail.DiscardErrorAsync(_ => Task.CompletedTask)).Or(-1));
        Assert.Equal(-1, (await tGenFail.DiscardErrorAsync(_ => Task.CompletedTask)).Or(-1));
        Assert.Equal(-1, (await Result.DiscardErrorAsync(new TestError("e").Fail<int, TestError>(), _ => Task.CompletedTask)).Or(-1));

        Assert.True((await tOk.DiscardValue()).Match(_ => true, _ => false));
        Assert.True((await tOk.DiscardValue(_ => Result.Ok())).Match(_ => true, _ => false));
        Assert.True((await tOk.DiscardValueAsync(_ => Task.FromResult(Result.Ok()))).Match(_ => true, _ => false));
        Assert.True((await tGenOk.DiscardValue()).Match(_ => true, _ => false));
        Assert.True((await tGenOk.DiscardValue(_ => Result.Ok<TestError>())).Match(_ => true, _ => false));
        Assert.True((await tGenOk.DiscardValueAsync(_ => Task.FromResult(Result.Ok<TestError>()))).Match(_ => true, _ => false));

        // ReturnOrValue / ReturnOrValueAsync / ReturnOrFail
        Assert.Equal(-1, await tFail.ReturnOrValue(_ => -1));
        Assert.Equal(-1, await tGenFail.ReturnOrValue(_ => -1));
        Assert.Equal(-1, await tFail.ReturnOrValueAsync(_ => Task.FromResult(-1)));
        Assert.Equal(-1, await tGenFail.ReturnOrValueAsync(_ => Task.FromResult(-1)));
        Assert.Equal(10, await tOk.ReturnOrFail(e => new InvalidOperationException(string.Join(",", e))));
        Assert.Equal(10, await tGenOk.ReturnOrFail(e => new InvalidOperationException(e.Message)));

        // Flatten (Task)
        Assert.True((await Task.FromResult(Result.Ok().Ok()).Flatten()).Match(_ => true, _ => false));
        Assert.Equal(5, (await Task.FromResult(5.Ok().Ok()).Flatten()).Match(v => v, _ => -1));
        Assert.Equal(5, (await Task.FromResult(5.Ok<int, TestError>().Ok<Result<int, TestError>, TestError>()).Flatten()).Match(v => v, _ => -1));
        Assert.True((await Task.FromResult(Result.Ok<TestError>().Ok<VoidResult<TestError>, TestError>()).Flatten()).Match(_ => true, _ => false));
        Assert.True((await Task.FromResult(Result.Ok<VoidResult<TestError>>()).Flatten()).Match(_ => true, _ => false));

        // Unwrap / UnwrapAsync
        Assert.True((await Task.CompletedTask.Ok().Unwrap<int>()).Match(_ => true, _ => false));
        Assert.Equal(42, (await Task.FromResult(42).Ok().Unwrap()).Match(v => v, _ => -1));
        Assert.Equal(42, (await Task.FromResult(42).Ok<Task<int>, TestError>().UnwrapAsync()).Match(v => v, _ => -1));
        Assert.Equal("e", (await Task.FromResult(new TestError("e")).Fail<int, Task<TestError>>().UnwrapAsync()).Match(_ => "", e => e.Message));
        Assert.True((await Task.CompletedTask.Ok<Task, TestError>().UnwrapAsync()).Match(_ => true, _ => false));
    }

    [Fact]
    public async Task Async_Fold_Reduce_Check_Then_and_Sequence()
    {
        IEnumerable<Result<int>> nums = [1.Ok(), 2.Ok(), 3.Ok()];
        IEnumerable<Result<int, TestError>> gNums = [1.Ok<int, TestError>(), 2.Ok<int, TestError>(), 3.Ok<int, TestError>()];
        static TestError Merge(TestError a, TestError b) => a.Plus(b);

        // FoldAsync / FoldUntilAsync
        Assert.Equal(6, (await nums.FoldAsync(0, (a, b) => Task.FromResult(a + b))).Match(v => v, _ => -1));
        Assert.Equal(6, (await nums.FoldAsync(0.Ok(), (a, b) => Task.FromResult(a + b))).Match(v => v, _ => -1));
        Assert.Equal(6, (await nums.FoldAsync(Task.FromResult(0.Ok()), (a, b) => Task.FromResult(a + b))).Match(v => v, _ => -1));
        Assert.Equal(6, (await gNums.FoldAsync(0, (a, b) => Task.FromResult(a + b))).Match(v => v, _ => -1));
        Assert.Equal(6, (await gNums.FoldAsync(0.Ok<int, TestError>(), (a, b) => Task.FromResult(a + b))).Match(v => v, _ => -1));
        Assert.Equal(6, (await gNums.FoldAsync(Task.FromResult(0.Ok<int, TestError>()), (a, b) => Task.FromResult(a + b))).Match(v => v, _ => -1));
        Assert.Equal(6, (await gNums.FoldAsync(0, Merge, (a, b) => Task.FromResult(a + b))).Match(v => v, _ => -1));
        Assert.Equal(6, (await gNums.FoldAsync(0.Ok<int, TestError>(), Merge, (a, b) => Task.FromResult(a + b))).Match(v => v, _ => -1));
        Assert.Equal(6, (await gNums.FoldAsync(Task.FromResult(0.Ok<int, TestError>()), Merge, (a, b) => Task.FromResult(a + b))).Match(v => v, _ => -1));

        Assert.Equal(6, (await nums.FoldUntilAsync(0, (a, b) => Task.FromResult(a + b))).Match(v => v, _ => -1));
        Assert.Equal(6, (await nums.FoldUntilAsync(0.Ok(), (a, b) => Task.FromResult(a + b))).Match(v => v, _ => -1));
        Assert.Equal(6, (await nums.FoldUntilAsync(Task.FromResult(0.Ok()), (a, b) => Task.FromResult(a + b))).Match(v => v, _ => -1));
        Assert.Equal(6, (await gNums.FoldUntilAsync(0, (a, b) => Task.FromResult(a + b))).Match(v => v, _ => -1));
        Assert.Equal(6, (await gNums.FoldUntilAsync(0.Ok<int, TestError>(), (a, b) => Task.FromResult(a + b))).Match(v => v, _ => -1));
        Assert.Equal(6, (await gNums.FoldUntilAsync(Task.FromResult(0.Ok<int, TestError>()), (a, b) => Task.FromResult(a + b))).Match(v => v, _ => -1));

        // ReduceAsync / ReduceUntilAsync / TryReduceAsync / TryReduceUntilAsync
        Assert.Equal(6, (await nums.ReduceAsync((a, b) => Task.FromResult(a + b))).Match(v => v, _ => -1));
        Assert.Equal(6, (await gNums.ReduceAsync((a, b) => Task.FromResult(a + b))).Match(v => v, _ => -1));
        Assert.Equal(6, (await gNums.ReduceAsync(Merge, (a, b) => Task.FromResult(a + b))).Match(v => v, _ => -1));
        Assert.Equal(6, (await nums.ReduceUntilAsync((a, b) => Task.FromResult(a + b))).Match(v => v, _ => -1));
        Assert.Equal(6, (await gNums.ReduceUntilAsync((a, b) => Task.FromResult(a + b))).Match(v => v, _ => -1));

        Assert.Equal(6, (await nums.TryReduceAsync((a, b) => Task.FromResult(a + b))).Or(0.Ok()).Match(v => v, _ => -1));
        Assert.Equal(6, (await gNums.TryReduceAsync((a, b) => Task.FromResult(a + b))).Or(0.Ok<int, TestError>()).Match(v => v, _ => -1));
        Assert.Equal(6, (await gNums.TryReduceAsync((a, b) => Task.FromResult(a + b), Merge)).Or(0.Ok<int, TestError>()).Match(v => v, _ => -1));
        Assert.Equal(6, (await nums.TryReduceUntilAsync((a, b) => Task.FromResult(a + b))).Or(0.Ok()).Match(v => v, _ => -1));
        Assert.Equal(6, (await gNums.TryReduceUntilAsync((a, b) => Task.FromResult(a + b))).Or(0.Ok<int, TestError>()).Match(v => v, _ => -1));

        // Check / CheckAsync
        Assert.Equal(10, (await Task.FromResult(10.Ok()).Check(x => x > 0 ? Result.Ok() : Result.Fail("neg"))).Match(v => v, _ => -1));
        Assert.Equal(10, (await Task.FromResult(10.Ok<int, TestError>()).Check(x => x > 0 ? Result.Ok<TestError>() : new TestError("neg").Fail())).Match(v => v, _ => -1));
        Assert.Equal(10, (await 10.Ok().CheckAsync(x => Task.FromResult(Result.Ok()))).Match(v => v, _ => -1));
        Assert.Equal(10, (await 10.Ok<int, TestError>().CheckAsync(x => Task.FromResult(Result.Ok<TestError>()))).Match(v => v, _ => -1));
        Assert.Equal(10, (await Task.FromResult(10.Ok()).CheckAsync(x => Task.FromResult(Result.Ok()))).Match(v => v, _ => -1));
        Assert.Equal(10, (await Task.FromResult(10.Ok<int, TestError>()).CheckAsync(x => Task.FromResult(Result.Ok<TestError>()))).Match(v => v, _ => -1));

        // Then / ThenAsync
        Assert.Equal(11, (await Task.FromResult(10.Ok()).Then(x => (x + 1).Ok())).Match(v => v, _ => -1));
        Assert.Equal(11, (await Task.FromResult(10.Ok<int, TestError>()).Then(x => (x + 1).Ok<int, TestError>())).Match(v => v, _ => -1));
        Assert.True((await Task.FromResult(Result.Ok()).Then(() => Result.Ok())).Match(_ => true, _ => false));
        Assert.True((await Task.FromResult(Result.Ok<TestError>()).Then(() => Result.Ok<TestError>())).Match(_ => true, _ => false));
        Assert.Equal(5, (await Task.FromResult(Result.Ok()).Then(() => 5.Ok())).Match(v => v, _ => -1));
        Assert.Equal(5, (await Task.FromResult(Result.Ok<TestError>()).Then(() => 5.Ok<int, TestError>())).Match(v => v, _ => -1));

        Assert.Equal(11, (await 10.Ok().ThenAsync(x => Task.FromResult((x + 1).Ok()))).Match(v => v, _ => -1));
        Assert.Equal(11, (await 10.Ok<int, TestError>().ThenAsync(x => Task.FromResult((x + 1).Ok<int, TestError>()))).Match(v => v, _ => -1));
        Assert.True((await Result.Ok().ThenAsync(() => Task.FromResult(Result.Ok()))).Match(_ => true, _ => false));
        Assert.True((await Result.Ok<TestError>().ThenAsync(() => Task.FromResult(Result.Ok<TestError>()))).Match(_ => true, _ => false));
        Assert.Equal(5, (await Result.Ok().ThenAsync(() => Task.FromResult(5.Ok()))).Match(v => v, _ => -1));
        Assert.Equal(5, (await Result.Ok<TestError>().ThenAsync(() => Task.FromResult(5.Ok<int, TestError>()))).Match(v => v, _ => -1));

        Assert.Equal(11, (await Task.FromResult(10.Ok()).ThenAsync(x => Task.FromResult((x + 1).Ok()))).Match(v => v, _ => -1));
        Assert.Equal(11, (await Task.FromResult(10.Ok<int, TestError>()).ThenAsync(x => Task.FromResult((x + 1).Ok<int, TestError>()))).Match(v => v, _ => -1));
        Assert.True((await Task.FromResult(Result.Ok()).ThenAsync(() => Task.FromResult(Result.Ok()))).Match(_ => true, _ => false));
        Assert.True((await Task.FromResult(Result.Ok<TestError>()).ThenAsync(() => Task.FromResult(Result.Ok<TestError>()))).Match(_ => true, _ => false));
        Assert.Equal(5, (await Task.FromResult(Result.Ok()).ThenAsync(() => Task.FromResult(5.Ok()))).Match(v => v, _ => -1));
        Assert.Equal(5, (await Task.FromResult(Result.Ok<TestError>()).ThenAsync(() => Task.FromResult(5.Ok<int, TestError>()))).Match(v => v, _ => -1));

        // SequenceAsync
        Assert.True((await Result.SequenceAsync(() => Task.FromResult(Result.Ok()), () => Task.FromResult(Result.Ok()))).Match(_ => true, _ => false));
        Assert.True((await Result.SequenceAsync(() => Task.FromResult(Result.Ok<TestError>()), () => Task.FromResult(Result.Ok<TestError>()))).Match(_ => true, _ => false));
    }
}
