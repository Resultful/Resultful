using Xunit;

namespace Resultful.Consumption;

public sealed class ResultErrorListTests
{
    [Fact]
    public void Default_and_bare_string_hazards_behave_as_shipped_today()
    {
        // Pins today's shipped behaviour (pre-M3 / Decisions 10 & 14):
        // 1. default(Result<T>) and new Result<T>() are Ok(default).
        // 2. A bare string implicitly converts to a failure, even for Result<string>.
        var fromCtor = new Result<int>();
        var fromDefault = default(Result<int>);
        Assert.Equal(0, fromCtor.Match(v => v, _ => -1));
        Assert.Equal(0, fromDefault.Match(v => v, _ => -1));

        Result<string> bareString = "oops";
        Assert.Equal(["oops"], bareString.Match(_ => Array.Empty<string>(), e => e.ToArray()));
    }

    [Fact]
    public void Default_is_a_success_for_every_result_type()
    {
        // Today every result struct's zero value is a success. Decision 10 makes it throw in 0.3 (08b).
        Assert.Equal(0, default(Result<int, TestError>).Match(v => v, _ => -1));
        Assert.Null(default(Result<string, TestError>).Match(v => v, _ => "failed"));
        Assert.Null(default(Result<string>).Match(v => v, _ => "failed"));

        // The VoidResult types succeed with a null Unit, not Unit.Value.
        Assert.True(default(VoidResult).Match(u => u is null, _ => false));
        Assert.True(default(VoidResult<TestError>).Match(u => u is null, _ => false));
        Assert.True(new VoidResult().Match(u => u is null, _ => false));
        Assert.True(new VoidResult<TestError>().Match(u => u is null, _ => false));
        Assert.Same(Unit.Value, Result.Ok().Match(u => u, _ => null!));

        // A default inside a collection is a success too, so it's silently folded in.
        Result<int>[] withDefault = [1.Ok(), default, 2.Ok()];
        Assert.Equal(3, withDefault.Fold(0, (a, b) => a + b).Match(v => v, _ => -1));
    }

    [Fact]
    public void A_bare_string_converts_to_an_error_wherever_a_Result_of_T_is_expected()
    {
        // Even when T is string, the conversion picks the error side.
        static Result<string> Greet(bool ok) => ok ? "hello".Ok() : "hello";
        Assert.Equal(["hello"], Greet(false).Match(_ => Array.Empty<string>(), e => e.ToArray()));
        Assert.Equal("hello", Greet(true).Match(v => v, _ => ""));

        // An empty string is still an error, with one empty message.
        Result<int> empty = string.Empty;
        Assert.Equal([string.Empty], empty.Match(_ => Array.Empty<string>(), e => e.ToArray()));

        // A string array and a List<string> convert to errors as well.
        Result<string> fromArray = new[] { "x", "y" };
        Result<string> fromList = new List<string> { "z" };
        Assert.Equal(["x", "y"], fromArray.Match(_ => Array.Empty<string>(), e => e.ToArray()));
        Assert.Equal(["z"], fromList.Match(_ => Array.Empty<string>(), e => e.ToArray()));
    }

    [Fact]
    public void Implicit_conversions_and_ToOneOf_cover_all_operators()
    {
        Result<int> fromString = "e1";
        Result<int> fromArray = new[] { "e1", "e2" };
        Result<int> fromList = new List<string> { "e3" };
        Result<int, IEnumerable<string>> generic = Result.Ok<int, IEnumerable<string>>(5);
        Result<int> fromGeneric = generic;

        Assert.Equal(["e1"], fromString.ToOneOf().AsT1.ToArray());
        Assert.Equal(["e1", "e2"], fromArray.ToOneOf().AsT1.ToArray());
        Assert.Equal(["e3"], fromList.ToOneOf().AsT1.ToArray());
        Assert.Equal(5, fromGeneric.ToOneOf().AsT0);
    }

    [Fact]
    public async Task Instance_sync_and_async_members_cover_success_and_failure_paths()
    {
        var ok = 10.Ok();
        Result<int> fail = "bad";

        Assert.Equal(20, ok.Bind(x => (x * 2).Ok()).Match(v => v, _ => -1));
        Assert.Equal(20, (await ok.BindAsync(x => Task.FromResult((x * 2).Ok()))).Match(v => v, _ => -1));

        Assert.Equal("10", ok.Map(x => x.ToString()).Match(v => v, _ => ""));
        Assert.Equal("10", (await ok.MapAsync(x => Task.FromResult(x.ToString()))).Match(v => v, _ => ""));
        Assert.Equal("10", ok.Map2(e => string.Join(",", e), x => x.ToString()).Match(v => v, e => e));
        Assert.Equal("bad", (await fail.Map2Async(e => Task.FromResult(string.Join(",", e)), x => Task.FromResult(x.ToString()))).Match(v => v, e => e));

        Assert.Equal("bad", fail.MapError(e => string.Join(",", e)).Match(_ => "", e => e));
        Assert.Equal("bad", (await fail.MapErrorAsync(e => Task.FromResult(string.Join(",", e)))).Match(_ => "", e => e));

        Result<object> boxed = ((object)"hi").Ok();
        Assert.Equal("hi", boxed.Cast<string>().Match(v => v, _ => ""));

        Assert.Equal(10, await ok.MatchAsync(x => Task.FromResult(x), _ => Task.FromResult(-1)));
        var switched = 0;
        ok.Switch(x => switched = x, _ => switched = -1);
        Assert.Equal(10, switched);
        // Pinned hazard: the struct SwitchAsync returns void and drops the callback's Task, so it
        // can't be awaited and a late exception is lost. Only the synchronous part is observable.
        Assert.Equal(typeof(void), typeof(Result<int>).GetMethod(nameof(Result<int>.SwitchAsync))!.ReturnType);
        ok.SwitchAsync(x => { switched = x + 1; return Task.CompletedTask; }, _ => { switched = -1; return Task.CompletedTask; });
        Assert.Equal(11, switched);

        var seen = 0;
        ok.Tee(x => seen = x);
        Assert.Equal(10, seen);
        await ok.TeeAsync(x => { seen = x * 2; return Task.CompletedTask; });
        Assert.Equal(20, seen);
        var seenErr = "";
        fail.TeeError(e => seenErr = string.Join(",", e));
        Assert.Equal("bad", seenErr);
        await fail.TeeErrorAsync(e => { seenErr = string.Join(",", e) + "!"; return Task.CompletedTask; });
        Assert.Equal("bad!", seenErr);

        Assert.Equal(10, ok.DiscardError().Or(-1));
        Assert.Equal(-1, fail.DiscardError(_ => seenErr = "d").Or(-1));
        Assert.Equal(-1, (await fail.DiscardErrorAsync(_ => { seenErr = "da"; return Task.CompletedTask; })).Or(-1));

        Assert.True(ok.DiscardValue().Match(_ => true, _ => false));
        Assert.True(ok.DiscardValue(_ => Result.Ok()).Match(_ => true, _ => false));
        Assert.True((await ok.DiscardValueAsync(_ => Task.FromResult(Result.Ok()))).Match(_ => true, _ => false));

        Assert.Equal(10, ok.ReturnOrValue(_ => -1));
        Assert.Equal(-1, fail.ReturnOrValue(_ => -1));
        Assert.Equal(-1, await fail.ReturnOrValueAsync(_ => Task.FromResult(-1)));
        Assert.Equal(10, ok.ReturnOrFail(e => new InvalidOperationException(string.Join(",", e))));
        Assert.Throws<InvalidOperationException>(() => fail.ReturnOrFail(e => new InvalidOperationException(string.Join(",", e))));
    }
}
