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

        var calls = new List<string>();
        Task Record(string call)
        {
            calls.Add(call);
            return Task.CompletedTask;
        }

        await tOk.Switch(v => calls.Add($"switch:{v}"), _ => calls.Add("switch:error"));
        await tGenOk.Switch(v => calls.Add($"genSwitch:{v}"), _ => calls.Add("genSwitch:error"));
        await tVoidOk.Switch<Unit>(_ => calls.Add("voidSwitch"), _ => calls.Add("voidSwitch:error"));
        await tGenVoidOk.Switch<Unit, TestError>(_ => calls.Add("genVoidSwitch"), _ => calls.Add("genVoidSwitch:error"));

        await tOk.SwitchAsync(v => Record($"switchAsync:{v}"), _ => Record("switchAsync:error"));
        await tGenOk.SwitchAsync(v => Record($"genSwitchAsync:{v}"), _ => Record("genSwitchAsync:error"));
        await tVoidOk.SwitchAsync<Unit>(_ => Record("voidSwitchAsync"), _ => Record("voidSwitchAsync:error"));
        await tGenVoidOk.SwitchAsync<Unit, TestError>(_ => Record("genVoidSwitchAsync"), _ => Record("genVoidSwitchAsync:error"));

        Assert.Equal(
            ["switch:10", "genSwitch:10", "voidSwitch", "genVoidSwitch",
             "switchAsync:10", "genSwitchAsync:10", "voidSwitchAsync", "genVoidSwitchAsync"],
            calls);
        calls.Clear();

        Assert.True((await tOk.ToOneOf()).IsT0);
        Assert.True((await tGenOk.ToOneOf()).IsT0);
        Assert.True((await tVoidOk.ToOneOf<Unit>()).IsT0);
        Assert.True((await tGenVoidOk.ToOneOf<Unit, TestError>()).IsT0);

        // Tee / TeeAsync / TeeError / TeeErrorAsync
        Assert.Equal(10, (await tOk.Tee(v => calls.Add($"tee:{v}"))).Match(v => v, _ => -1));
        Assert.Equal(10, (await tGenOk.Tee(v => calls.Add($"genTee:{v}"))).Match(v => v, _ => -1));
        Assert.Equal(Outcome.Succeeded, (await tVoidOk.Tee(_ => calls.Add("voidTee"))).Errors().Single());
        Assert.Equal(Outcome.Succeeded, (await tGenVoidOk.Tee(_ => calls.Add("genVoidTee"))).Error());

        Assert.Equal(10, (await tOk.TeeAsync(v => Record($"teeAsync:{v}"))).Match(v => v, _ => -1));
        Assert.Equal(10, (await tGenOk.TeeAsync(v => Record($"genTeeAsync:{v}"))).Match(v => v, _ => -1));
        Assert.Equal(Outcome.Succeeded, (await tVoidOk.TeeAsync(_ => Record("voidTeeAsync"))).Errors().Single());
        Assert.Equal(Outcome.Succeeded, (await tGenVoidOk.TeeAsync(_ => Record("genVoidTeeAsync"))).Error());

        Assert.Equal(["bad"], (await tFail.TeeError(e => calls.Add($"teeError:{string.Join(",", e)}"))).Errors());
        Assert.Equal("bad", (await tGenFail.TeeError(e => calls.Add($"genTeeError:{e.Message}"))).Error());
        Assert.Equal(["bad"], (await tVoidFail.TeeError(e => calls.Add($"voidTeeError:{string.Join(",", e)}"))).Errors());
        Assert.Equal("bad", (await tGenVoidFail.TeeError(e => calls.Add($"genVoidTeeError:{e.Message}"))).Error());

        Assert.Equal(["bad"], (await tFail.TeeErrorAsync(e => Record($"teeErrorAsync:{string.Join(",", e)}"))).Errors());
        Assert.Equal("bad", (await tGenFail.TeeErrorAsync(e => Record($"genTeeErrorAsync:{e.Message}"))).Error());
        Assert.Equal(["bad"], (await tVoidFail.TeeErrorAsync(e => Record($"voidTeeErrorAsync:{string.Join(",", e)}"))).Errors());
        Assert.Equal("bad", (await tGenVoidFail.TeeErrorAsync(e => Record($"genVoidTeeErrorAsync:{e.Message}"))).Error());

        Assert.Equal(
            ["tee:10", "genTee:10", "voidTee", "genVoidTee",
             "teeAsync:10", "genTeeAsync:10", "voidTeeAsync", "genVoidTeeAsync",
             "teeError:bad", "genTeeError:bad", "voidTeeError:bad", "genVoidTeeError:bad",
             "teeErrorAsync:bad", "genTeeErrorAsync:bad", "voidTeeErrorAsync:bad", "genVoidTeeErrorAsync:bad"],
            calls);

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

    // Failure paths and mixed inputs. Each async operator gives the same outcome as its sync twin.

    [Fact]
    public async Task Async_Fold_and_Reduce_collect_every_error_like_the_sync_versions()
    {
        var aggrCalls = 0;
        Task<int> AddAsync(int a, int b)
        {
            aggrCalls++;
            return Task.FromResult(a + b);
        }

        static TestError Merge(TestError left, TestError right) => new($"{left.Message}+{right.Message}");

        IEnumerable<Result<int>> mixed = [1.Ok(), Result.Fail<int>("a"), 3.Ok(), Result.Fail<int>("b", "c")];
        Assert.Equal(mixed.Fold(0, (a, b) => a + b).Errors(), (await mixed.FoldAsync(0, AddAsync)).Errors());
        Assert.Equal(["a", "b", "c"], (await mixed.FoldAsync(0, AddAsync)).Errors());
        Assert.Equal(["s", "a", "b", "c"], (await mixed.FoldAsync(Result.Fail<int>("s"), AddAsync)).Errors());
        Assert.Equal(["s", "a", "b", "c"], (await mixed.FoldAsync(Task.FromResult(Result.Fail<int>("s")), AddAsync)).Errors());
        Assert.Equal(["a", "b", "c"], (await mixed.ReduceAsync(AddAsync)).Errors());
        Assert.Equal(["a", "b", "c"], (await mixed.TryReduceAsync(AddAsync)).Map(r => r.Errors()).Or([]));

        // Each FoldAsync from an Ok seed aggregates the first value, then stops aggregating.
        Assert.Equal(2, aggrCalls);

        IEnumerable<Result<int, TestError>> gMixed =
            [1.Ok<int, TestError>(), new TestError("a").Fail<int, TestError>(), 3.Ok<int, TestError>(), new TestError("b").Fail<int, TestError>()];
        Assert.Equal("a; b", (await gMixed.FoldAsync(0, AddAsync)).Error());
        Assert.Equal("a+b", (await gMixed.FoldAsync(0, Merge, AddAsync)).Error());
        Assert.Equal("s+a+b", (await gMixed.FoldAsync(new TestError("s").Fail<int, TestError>(), Merge, AddAsync)).Error());
        Assert.Equal("s; a; b", (await gMixed.FoldAsync(Task.FromResult(new TestError("s").Fail<int, TestError>()), AddAsync)).Error());
        Assert.Equal("a; b", (await gMixed.ReduceAsync(AddAsync)).Error());
        Assert.Equal("a+b", (await gMixed.ReduceAsync(Merge, AddAsync)).Error());
        Assert.Equal("a; b", (await gMixed.TryReduceAsync(AddAsync)).Map(r => r.Error()).Or(""));
        Assert.Equal("a+b", (await gMixed.TryReduceAsync(AddAsync, Merge)).Map(r => r.Error()).Or(""));
        Assert.Equal(4, aggrCalls);

        // Empty input: Fold gives the seed, Reduce throws, TryReduce gives None.
        Assert.Equal(7, (await Array.Empty<Result<int>>().FoldAsync(7, AddAsync)).Match(v => v, _ => -1));
        await Assert.ThrowsAsync<ArgumentException>(() => Array.Empty<Result<int>>().ReduceAsync(AddAsync));
        await Assert.ThrowsAsync<ArgumentException>(() => Array.Empty<Result<int, TestError>>().ReduceAsync(AddAsync));
        Assert.False((await Array.Empty<Result<int>>().TryReduceAsync(AddAsync)).Map(_ => true).Or(false));
        Assert.False((await Array.Empty<Result<int, TestError>>().TryReduceUntilAsync(AddAsync)).Map(_ => true).Or(false));
    }

    [Fact]
    public async Task Async_Until_variants_keep_the_first_failure_and_stop_pulling_the_source()
    {
        var aggrCalls = 0;
        Task<int> AddAsync(int a, int b)
        {
            aggrCalls++;
            return Task.FromResult(a + b);
        }

        Result<int>[] items = [1.Ok(), Result.Fail<int>("a"), Result.Fail<int>("b"), 4.Ok()];
        Result<int, TestError>[] gItems =
            [1.Ok<int, TestError>(), new TestError("a").Fail<int, TestError>(), new TestError("b").Fail<int, TestError>(), 4.Ok<int, TestError>()];

        // Same pull count as the sync versions: one past the first failure.
        var pulled = 0;
        Assert.Equal(["a"], (await Outcome.Counted(items, () => pulled++).FoldUntilAsync(0, AddAsync)).Errors());
        Assert.Equal(3, pulled);

        pulled = 0;
        Assert.Equal(["a"], (await Outcome.Counted(items, () => pulled++).ReduceUntilAsync(AddAsync)).Errors());
        Assert.Equal(3, pulled);

        pulled = 0;
        Assert.Equal(["a"], (await Outcome.Counted(items, () => pulled++).TryReduceUntilAsync(AddAsync)).Map(r => r.Errors()).Or([]));
        Assert.Equal(3, pulled);

        pulled = 0;
        Assert.Equal("a", (await Outcome.Counted(gItems, () => pulled++).FoldUntilAsync(0, AddAsync)).Error());
        Assert.Equal(3, pulled);

        pulled = 0;
        Assert.Equal("a", (await Outcome.Counted(gItems, () => pulled++).ReduceUntilAsync(AddAsync)).Error());
        Assert.Equal(3, pulled);

        pulled = 0;
        Assert.Equal("a", (await Outcome.Counted(gItems, () => pulled++).TryReduceUntilAsync(AddAsync)).Map(r => r.Error()).Or(""));
        Assert.Equal(3, pulled);

        pulled = 0;
        Assert.Equal(["s"], (await Outcome.Counted(items, () => pulled++).FoldUntilAsync(Task.FromResult(Result.Fail<int>("s")), AddAsync)).Errors());
        Assert.Equal(1, pulled);

        pulled = 0;
        Assert.Equal("s", (await Outcome.Counted(gItems, () => pulled++).FoldUntilAsync(new TestError("s").Fail<int, TestError>(), AddAsync)).Error());
        Assert.Equal(1, pulled);

        // The aggregator ran once per FoldUntil, for the value before the first failure.
        Assert.Equal(2, aggrCalls);
        Assert.Equal(
            items.FoldUntil(0, (a, b) => a + b).Errors(),
            (await items.FoldUntilAsync(0, AddAsync)).Errors());
    }

    [Fact]
    public async Task Async_Then_Check_and_Sequence_return_the_first_failure_and_skip_later_steps()
    {
        var later = 0;
        Task<VoidResult> LaterVoid()
        {
            later++;
            return Task.FromResult(Result.Fail("later"));
        }

        Task<VoidResult<TestError>> LaterGVoid()
        {
            later++;
            return Task.FromResult(new TestError("later").Fail());
        }

        Assert.Equal(["a"], (await Result.SequenceAsync(() => Task.FromResult(Result.Fail("a")), LaterVoid)).Errors());
        Assert.Equal("a", (await Result.SequenceAsync(() => Task.FromResult(new TestError("a").Fail()), LaterGVoid)).Error());
        Assert.Equal(["pre"], (await Result.Fail("pre").ThenAsync(LaterVoid)).Errors());
        Assert.Equal("pre", (await new TestError("pre").Fail().ThenAsync(LaterGVoid)).Error());
        Assert.Equal(["pre"], (await Task.FromResult(Result.Fail("pre")).ThenAsync(LaterVoid)).Errors());
        Assert.Equal(["pre"], (await Task.FromResult(Result.Fail("pre")).Then(() => { later++; return Result.Ok(); })).Errors());
        Assert.Equal("pre", (await Task.FromResult(new TestError("pre").Fail()).Then(() => { later++; return 1.Ok<int, TestError>(); })).Error());
        Assert.Equal(["pre"], (await Result.Fail("pre").ThenAsync(() => { later++; return Task.FromResult(1.Ok()); })).Errors());

        Assert.Equal(["a"], (await 10.Ok().ThenAsync(_ => Task.FromResult(Result.Fail<int>("a")), x => { later++; return Task.FromResult(x.Ok()); })).Errors());
        Assert.Equal("a", (await Task.FromResult(10.Ok<int, TestError>()).ThenAsync(_ => Task.FromResult(new TestError("a").Fail<int, TestError>()), x => { later++; return Task.FromResult(x.Ok<int, TestError>()); })).Error());
        Assert.Equal(["pre"], (await Task.FromResult(Result.Fail<int>("pre")).Then(x => { later++; return x.Ok(); })).Errors());

        Assert.Equal(["c1"], (await 10.Ok().CheckAsync(_ => Task.FromResult(Result.Fail("c1")), _ => LaterVoid())).Errors());
        Assert.Equal("c1", (await 10.Ok<int, TestError>().CheckAsync(_ => Task.FromResult(new TestError("c1").Fail()), _ => LaterGVoid())).Error());
        Assert.Equal(["c1"], (await Task.FromResult(10.Ok()).Check(_ => Result.Fail("c1"), _ => { later++; return Result.Ok(); })).Errors());
        Assert.Equal("pre", (await Task.FromResult(new TestError("pre").Fail<int, TestError>()).CheckAsync(_ => LaterGVoid())).Error());
        Assert.Equal(0, later);
    }

    [Fact]
    public async Task Task_of_failed_result_skips_success_callbacks_and_runs_error_callbacks()
    {
        var successCalls = 0;
        var errorCalls = new List<string>();
        var failed = Task.FromResult(Result.Fail<int>("e"));
        var gFailed = Task.FromResult(new TestError("e").Fail<int, TestError>());
        var vFailed = Task.FromResult(Result.Fail("e"));
        var gvFailed = Task.FromResult(new TestError("e").Fail());

        Assert.Equal(["e"], (await failed.Map(x => { successCalls++; return x; })).Errors());
        Assert.Equal(["e"], (await failed.Bind(x => { successCalls++; return x.Ok(); })).Errors());
        Assert.Equal(["e"], (await failed.BindAsync(x => { successCalls++; return Task.FromResult(x.Ok()); })).Errors());
        Assert.Equal(["e"], (await failed.Tee(_ => successCalls++)).Errors());
        Assert.Equal(["e"], (await failed.TeeError(e => errorCalls.AddRange(e))).Errors());
        Assert.Equal("E", (await failed.MapError(e => new TestError(e.Single().ToUpperInvariant()))).Error());
        Assert.Equal("e!", (await gFailed.MapError(e => new TestError(e.Message + "!"))).Error());
        Assert.Equal("e", (await gFailed.Map(x => { successCalls++; return x; })).Error());
        Assert.Equal("e", (await gFailed.TeeErrorAsync(e => { errorCalls.Add(e.Message); return Task.CompletedTask; })).Error());
        Assert.Equal(["e"], (await vFailed.Bind(_ => { successCalls++; return Result.Ok(); })).Errors());
        Assert.Equal(["e"], (await vFailed.BindValue(_ => { successCalls++; return 1.Ok(); })).Errors());
        Assert.Equal("e", (await gvFailed.Tee(_ => successCalls++)).Error());
        Assert.Equal(0, successCalls);
        Assert.Equal(["e", "e"], errorCalls);

        // Leaving the result: error handlers decide the value, or throw.
        Assert.Equal(-1, await failed.ReturnOrValue(_ => -1));
        Assert.Equal(-2, await gFailed.ReturnOrValueAsync(_ => Task.FromResult(-2)));
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => failed.ReturnOrFail(e => new InvalidOperationException(string.Join(",", e))));
        Assert.Equal("e", thrown.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => gFailed.ReturnOrFail(e => new InvalidOperationException(e.Message)));

        var discarded = new List<string>();
        Assert.False((await failed.DiscardError(e => discarded.AddRange(e))).Map(_ => true).Or(false));
        Assert.False((await gFailed.DiscardError()).Map(_ => true).Or(false));
        Assert.Equal(["e"], discarded);
        Assert.Equal(["e"], (await failed.DiscardValue()).Errors());
        Assert.Equal("e", (await gFailed.DiscardValue()).Error());

        // A failed Task-valued result is not awaited, and keeps its errors.
        Assert.Equal(["e"], (await Result.Fail<Task<int>>("e").Unwrap()).Errors());
        Assert.Equal("e", (await new TestError("e").Fail<Task<int>, TestError>().UnwrapAsync()).Error());
    }
}
