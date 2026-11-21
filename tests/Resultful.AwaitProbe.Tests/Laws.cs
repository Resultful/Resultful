using System.Runtime.CompilerServices;
using CsCheck;
using Resultful.AwaitProbe.ShortCircuit;

namespace Resultful.AwaitProbe.Tests;

/// <summary>An await-style method agrees with the Bind chain that writes the same steps, for every builder.</summary>
public sealed class Laws
{
    static Result<int> Step(int acc, bool ok, int index) => ok ? acc + index : Error.From($"step {index} failed");

    static Result<int> ByBind(bool[] steps) =>
        steps.Select((ok, i) => (ok, i)).Aggregate(0.Ok(), (acc, s) => acc.Bind(a => Step(a, s.ok, s.i)));

    [AsyncMethodBuilder(typeof(ResultBuilder<>))]
    static async Result<int> ByAwait(bool[] steps)
    {
        var acc = 0;
        for (var i = 0; i < steps.Length; i++) acc = await Step(acc, steps[i], i);
        return acc;
    }

    [AsyncMethodBuilder(typeof(UnwindingResultBuilder<>))]
    static async Result<int> ByUnwinding(bool[] steps)
    {
        var acc = 0;
        for (var i = 0; i < steps.Length; i++) acc = await Step(acc, steps[i], i);
        return acc;
    }

    [AsyncMethodBuilder(typeof(AsyncResultBuilder<>))]
    static async ValueTask<Result<int>> ByAsync(bool[] steps, bool yield)
    {
        var acc = 0;
        for (var i = 0; i < steps.Length; i++)
        {
            if (yield) await Task.Yield();
            acc = await Step(acc, steps[i], i);
        }

        return acc;
    }

    [AsyncMethodBuilder(typeof(UnwindingAsyncResultBuilder<>))]
    static async ValueTask<Result<int>> ByAsyncUnwinding(bool[] steps, bool yield)
    {
        var acc = 0;
        for (var i = 0; i < steps.Length; i++)
        {
            if (yield) await Task.Yield();
            acc = await Step(acc, steps[i], i);
        }

        return acc;
    }

    static readonly Gen<bool[]> Steps = Gen.Frequency((4, Gen.Const(true)), (1, Gen.Const(false))).Array[0, 12];

    [Fact]
    public void Sync_builders_agree_with_Bind() =>
        Steps.Sample(steps => ByAwait(steps) == ByBind(steps) && ByUnwinding(steps) == ByBind(steps));

    [Fact]
    public void Async_builders_agree_with_Bind_with_and_without_real_suspensions() =>
        Steps.Select(Gen.Bool).Sample((steps, yield) =>
            ByAsync(steps, yield).AsTask().Result == ByBind(steps) && ByAsyncUnwinding(steps, yield).AsTask().Result == ByBind(steps), iter: 200);

    [Fact]
    public void Returning_an_awaited_success_is_the_success_and_an_awaited_failure_is_the_failure() =>
        Gen.Int.Select(Gen.Bool).Sample((value, ok) =>
        {
            var source = ok ? value.Ok() : Error.From($"failed {value}").Fail<int>();
            return Echo(source) == source;
        });

    [AsyncMethodBuilder(typeof(ResultBuilder<>))]
    static async Result<int> Echo(Result<int> source) => await source;
}
