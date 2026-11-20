using OneOf;
using Xunit;

namespace Resultful.Consumption;

public sealed class ResultGenericTests
{
    [Fact]
    public void Default_and_parameterless_ctor_behave_as_shipped_today()
    {
        // Pins today's shipped behaviour (pre-M3 / Decision 10): default and new Result<T, TError>() are Ok(default).
        var fromCtor = new Result<int, TestError>();
        var fromDefault = default(Result<int, TestError>);

        Assert.Equal(0, fromCtor.Match(v => v, _ => -1));
        Assert.Equal(0, fromDefault.Match(v => v, _ => -1));
    }

    [Fact]
    public void Implicit_conversions_and_ToOneOf_roundtrip()
    {
        Result<int, TestError> ok = 42;
        Result<int, TestError> fail = new TestError("boom");

        OneOf<int, TestError> okOneOf = ok.ToOneOf();
        OneOf<int, TestError> failOneOf = fail.ToOneOf();

        Assert.True(okOneOf.IsT0);
        Assert.Equal(42, okOneOf.AsT0);
        Assert.True(failOneOf.IsT1);
        Assert.Equal("boom", failOneOf.AsT1.Message);
    }

    [Fact]
    public async Task Instance_sync_and_async_members_cover_success_and_failure_paths()
    {
        Result<int, TestError> ok = 10;
        Result<int, TestError> fail = new TestError("err");

        // Bind / BindAsync
        Assert.Equal(20, ok.Bind(x => (x * 2).Ok<int, TestError>()).Match(v => v, _ => -1));
        Assert.Equal("err", fail.Bind(x => (x * 2).Ok<int, TestError>()).Match(_ => "", e => e.Message));
        Assert.Equal(20, (await ok.BindAsync(x => Task.FromResult((x * 2).Ok<int, TestError>()))).Match(v => v, _ => -1));

        // Map / MapAsync / Map2 / Map2Async / MapError / MapErrorAsync
        Assert.Equal("10", ok.Map(x => x.ToString()).Match(v => v, _ => ""));
        Assert.Equal("10", (await ok.MapAsync(x => Task.FromResult(x.ToString()))).Match(v => v, _ => ""));
        Assert.Equal("10", ok.Map2(e => e.Message, x => x.ToString()).Match(v => v, e => e));
        Assert.Equal("err", fail.Map2(e => e.Message, x => x.ToString()).Match(v => v, e => e));
        Assert.Equal("10", (await ok.Map2Async(e => Task.FromResult(e.Message), x => Task.FromResult(x.ToString()))).Match(v => v, e => e));

        Assert.Equal("ERR", fail.MapError(e => e.Message.ToUpperInvariant()).Match(_ => "", e => e));
        Assert.Equal(["err"], fail.MapError(e => (IEnumerable<string>)[e.Message]).Match(_ => Array.Empty<string>(), e => e.ToArray()));
        Assert.Equal("ERR", (await fail.MapErrorAsync(e => Task.FromResult(e.Message.ToUpperInvariant()))).Match(_ => "", e => e));
        Assert.Equal(["err"], (await fail.MapErrorAsync(e => Task.FromResult<IEnumerable<string>>([e.Message]))).Match(_ => Array.Empty<string>(), e => e.ToArray()));

        // Cast / CastError
        Result<object, TestError> boxed = "hello";
        Assert.Equal("hello", boxed.Cast<string>().Match(v => v, _ => ""));
        Result<int, object> boxedErr = Result.Fail<int, object>("e");
        Assert.Equal("e", boxedErr.CastError<string>().Match(_ => "", e => e));

        // Match / MatchAsync / Switch / SwitchAsync
        Assert.Equal(10, await ok.MatchAsync(x => Task.FromResult(x), _ => Task.FromResult(-1)));
        var switched = 0;
        ok.Switch(x => switched = x, _ => switched = -1);
        Assert.Equal(10, switched);
        ok.SwitchAsync(x => { switched = x + 1; return Task.CompletedTask; }, _ => Task.CompletedTask);
        Assert.Equal(11, switched);

        // Tee / TeeAsync / TeeError / TeeErrorAsync
        var seen = 0;
        ok.Tee(x => seen = x);
        Assert.Equal(10, seen);
        await ok.TeeAsync(x => { seen = x * 2; return Task.CompletedTask; });
        Assert.Equal(20, seen);
        var seenErr = "";
        fail.TeeError(e => seenErr = e.Message);
        Assert.Equal("err", seenErr);
        await fail.TeeErrorAsync(e => { seenErr = e.Message + "!"; return Task.CompletedTask; });
        Assert.Equal("err!", seenErr);

        // DiscardError / DiscardErrorAsync / DiscardValue / DiscardValueAsync
        Assert.Equal(10, ok.DiscardError().Or(-1));
        Assert.Equal(-1, fail.DiscardError(_ => seenErr = "d").Or(-1));
        Assert.Equal("d", seenErr);
        Assert.Equal(-1, (await fail.DiscardErrorAsync(_ => { seenErr = "da"; return Task.CompletedTask; })).Or(-1));
        Assert.Equal("da", seenErr);

        Assert.True(ok.DiscardValue().Match(_ => true, _ => false));
        Assert.True(ok.DiscardValue(_ => Result.Ok<TestError>()).Match(_ => true, _ => false));
        Assert.True((await ok.DiscardValueAsync(_ => Task.FromResult(Result.Ok<TestError>()))).Match(_ => true, _ => false));

        // ReturnOrValue / ReturnOrValueAsync / ReturnOrFail
        Assert.Equal(10, ok.ReturnOrValue(_ => -1));
        Assert.Equal(-1, fail.ReturnOrValue(_ => -1));
        Assert.Equal(-1, await fail.ReturnOrValueAsync(_ => Task.FromResult(-1)));
        Assert.Equal(10, ok.ReturnOrFail(e => new InvalidOperationException(e.Message)));
        Assert.Throws<InvalidOperationException>(() => fail.ReturnOrFail(e => new InvalidOperationException(e.Message)));
    }
}
