using Xunit;

namespace Resultful.Consumption;

public sealed class VoidResultTests
{
    [Fact]
    public void Shipped_Unit_is_a_class_Result_Ok_returns_VoidResult_and_ResultOfUnit_converts_to_VoidResult()
    {
        // Pins today's shipped behaviour (pre-M3 / Decision 14):
        // Unit is a reference type, Result.Ok() returns VoidResult, and Result<Unit> converts to VoidResult.
        Assert.True(typeof(Unit).IsClass);
        Assert.NotNull(Unit.Value);

        var ok = Result.Ok();
        Assert.Equal([Outcome.Succeeded], ok.Errors());
        Assert.True(ok.Match(u => ReferenceEquals(u, Unit.Value), _ => false));

        Result<Unit> resultOfUnit = Unit.Value.Ok();
        VoidResult converted = resultOfUnit;
        Assert.True(converted.Match(u => ReferenceEquals(u, Unit.Value), _ => false));
    }

    [Fact]
    public void Default_parameterless_ctor_and_implicit_conversions_for_VoidResult()
    {
        var fromCtor = new VoidResult();
        Assert.True(fromCtor.Match(_ => true, _ => false));

        VoidResult fromUnit = Unit.Value;
        VoidResult fromString = "e1";
        VoidResult fromArray = new[] { "e1", "e2" };
        VoidResult fromList = new List<string> { "e3" };
        VoidResult fromResultUnit = Unit.Value.Ok();
        VoidResult fromGenericResultUnit = Result.Ok<Unit, IEnumerable<string>>(Unit.Value);
        VoidResult fromGenericVoid = Result.Ok<IEnumerable<string>>();

        Assert.True(fromUnit.ToOneOf().IsT0);
        Assert.Equal(["e1"], fromString.ToOneOf().AsT1.ToArray());
        Assert.Equal(["e1", "e2"], fromArray.ToOneOf().AsT1.ToArray());
        Assert.Equal(["e3"], fromList.ToOneOf().AsT1.ToArray());
        Assert.True(fromResultUnit.Match(_ => true, _ => false));
        Assert.True(fromGenericResultUnit.Match(_ => true, _ => false));
        Assert.True(fromGenericVoid.Match(_ => true, _ => false));
    }

    [Fact]
    public async Task VoidResult_instance_members_sync_and_async()
    {
        var ok = Result.Ok();
        VoidResult fail = "bad";

        Assert.True(ok.Bind(_ => Result.Ok()).Match(_ => true, _ => false));
        Assert.True((await ok.BindAsync(_ => Task.FromResult(Result.Ok()))).Match(_ => true, _ => false));
        Assert.Equal(5, ok.BindValue(_ => 5.Ok()).Match(v => v, _ => -1));
        Assert.Equal(5, (await ok.BindValueAsync(_ => Task.FromResult(5.Ok()))).Match(v => v, _ => -1));

        Assert.Equal(7, ok.Map(7).Match(v => v, _ => -1));
        Assert.Equal(7, ok.Map(_ => 7).Match(v => v, _ => -1));
        Assert.Equal(7, (await ok.MapAsync(Task.FromResult(7))).Match(v => v, _ => -1));
        Assert.Equal(7, (await ok.MapAsync(_ => Task.FromResult(7))).Match(v => v, _ => -1));

        Assert.Equal(7, ok.Map2(e => string.Join(",", e), _ => 7).Match(v => v, _ => -1));
        Assert.Equal("bad", (await fail.Map2Async(e => Task.FromResult(string.Join(",", e)), () => Task.FromResult(7))).Match(_ => "", e => e));

        Assert.Equal(["BAD"], fail.MapError(e => e.Select(s => s.ToUpperInvariant())).Match(_ => Array.Empty<string>(), e => e.ToArray()));
        Assert.Equal("bad", fail.MapError(e => string.Join(",", e)).Match(_ => "", e => e));
        Assert.Equal(["BAD"], (await fail.MapErrorAsync(e => Task.FromResult(e.Select(s => s.ToUpperInvariant())))).Match(_ => Array.Empty<string>(), e => e.ToArray()));
        Assert.Equal("bad", (await fail.MapErrorAsync(e => Task.FromResult(string.Join(",", e)))).Match(_ => "", e => e));

        Assert.Equal(1, await ok.MatchAsync(_ => Task.FromResult(1), _ => Task.FromResult(0)));
        var hit = "";
        ok.Switch(_ => hit = "switch", _ => hit = "switch-error");
        Assert.Equal("switch", hit);
        // Pinned hazard: the struct SwitchAsync returns void and drops the callback's Task, so it
        // can't be awaited and a late exception is lost. Only the synchronous part is observable.
        Assert.Equal(typeof(void), typeof(VoidResult).GetMethod(nameof(VoidResult.SwitchAsync))!.ReturnType);
        ok.SwitchAsync(_ => { hit = "switchAsync"; return Task.CompletedTask; }, _ => { hit = "switchAsync-error"; return Task.CompletedTask; });
        Assert.Equal("switchAsync", hit);

        ok.Tee(_ => hit = "tee");
        Assert.Equal("tee", hit);
        await ok.TeeAsync(_ => { hit = "teeAsync"; return Task.CompletedTask; });
        Assert.Equal("teeAsync", hit);
        var errMsg = "";
        fail.TeeError(e => errMsg = string.Join(",", e));
        Assert.Equal("bad", errMsg);
        await fail.TeeErrorAsync(e => { errMsg = string.Join(",", e) + "!"; return Task.CompletedTask; });
        Assert.Equal("bad!", errMsg);
    }

    [Fact]
    public async Task VoidResult_generic_ctor_conversions_and_instance_members()
    {
        var fromCtor = new VoidResult<TestError>();
        Assert.True(fromCtor.Match(_ => true, _ => false));

        VoidResult<TestError> fromUnit = Unit.Value;
        VoidResult<TestError> fromErr = new TestError("e");
        VoidResult<TestError> fromResultUnit = Result.Ok<Unit, TestError>(Unit.Value);

        Assert.True(fromUnit.ToOneOf().IsT0);
        Assert.Equal("e", fromErr.ToOneOf().AsT1.Message);
        Assert.True(fromResultUnit.Match(_ => true, _ => false));

        var ok = Result.Ok<TestError>();
        var fail = new TestError("e").Fail();

        Assert.True(ok.Bind(_ => Result.Ok<TestError>()).Match(_ => true, _ => false));
        Assert.True((await ok.BindAsync(_ => Task.FromResult(Result.Ok<TestError>()))).Match(_ => true, _ => false));
        Assert.Equal(9, ok.BindValue(_ => 9.Ok<int, TestError>()).Match(v => v, _ => -1));
        Assert.Equal(9, (await ok.BindValueAsync(_ => Task.FromResult(9.Ok<int, TestError>()))).Match(v => v, _ => -1));

        VoidResult<object> boxedErr = ((object)"x").Fail();
        Assert.Equal("x", boxedErr.Cast<string>().Match(_ => "", e => e));

        Assert.Equal(3, ok.Map(3).Match(v => v, _ => -1));
        Assert.Equal(3, ok.Map(_ => 3).Match(v => v, _ => -1));
        Assert.Equal(3, (await ok.MapAsync(Task.FromResult(3))).Match(v => v, _ => -1));
        Assert.Equal(3, (await ok.MapAsync(_ => Task.FromResult(3))).Match(v => v, _ => -1));
        Assert.Equal(3, ok.Map2(e => e.Message, _ => 3).Match(v => v, _ => -1));
        Assert.Equal("e", (await fail.Map2Async(e => Task.FromResult(e.Message), () => Task.FromResult(3))).Match(_ => "", e => e));

        Assert.Equal(["e"], fail.MapError(e => (IEnumerable<string>)[e.Message]).Match(_ => Array.Empty<string>(), e => e.ToArray()));
        Assert.Equal("e", fail.MapError(e => e.Message).Match(_ => "", e => e));
        Assert.Equal(["e"], (await fail.MapErrorAsync(e => Task.FromResult<IEnumerable<string>>([e.Message]))).Match(_ => Array.Empty<string>(), e => e.ToArray()));
        Assert.Equal("e", (await fail.MapErrorAsync(e => Task.FromResult(e.Message))).Match(_ => "", e => e));

        Assert.Equal(1, await ok.MatchAsync(_ => Task.FromResult(1), _ => Task.FromResult(0)));
        var hit = "";
        ok.Switch(_ => hit = "switch", _ => hit = "switch-error");
        Assert.Equal("switch", hit);
        // Pinned hazard: the struct SwitchAsync returns void and drops the callback's Task, so it
        // can't be awaited and a late exception is lost. Only the synchronous part is observable.
        Assert.Equal(typeof(void), typeof(VoidResult<TestError>).GetMethod(nameof(VoidResult<TestError>.SwitchAsync))!.ReturnType);
        ok.SwitchAsync(_ => { hit = "switchAsync"; return Task.CompletedTask; }, _ => { hit = "switchAsync-error"; return Task.CompletedTask; });
        Assert.Equal("switchAsync", hit);

        ok.Tee(_ => hit = "tee");
        Assert.Equal("tee", hit);
        await ok.TeeAsync(_ => { hit = "teeAsync"; return Task.CompletedTask; });
        Assert.Equal("teeAsync", hit);
        var errMsg = "";
        fail.TeeError(e => errMsg = e.Message);
        Assert.Equal("e", errMsg);
        await fail.TeeErrorAsync(e => { errMsg = e.Message + "!"; return Task.CompletedTask; });
        Assert.Equal("e!", errMsg);
    }
}
