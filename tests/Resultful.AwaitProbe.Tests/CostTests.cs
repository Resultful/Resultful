using System.Runtime.CompilerServices;
using Resultful.AwaitProbe.ShortCircuit;

namespace Resultful.AwaitProbe.Tests;

/// <summary>Allocations per call of the same three-step pipeline in each style, measured on the test's thread.</summary>
public sealed class CostTests(ITestOutputHelper output)
{
    const int Max = 10;
    static readonly Error Failed = Error.From("failed");

    static Result<int> Step(int x, int max) => x <= max ? x + 1 : Failed;

    [AsyncMethodBuilder(typeof(ResultBuilder<>))]
    static async Result<int> Await(int start, int max)
    {
        var a = await Step(start, max);
        var b = await Step(a, max);
        return await Step(b, max);
    }

    [AsyncMethodBuilder(typeof(UnwindingResultBuilder<>))]
    static async Result<int> Unwinding(int start, int max)
    {
        var a = await Step(start, max);
        var b = await Step(a, max);
        return await Step(b, max);
    }

    static Result<int> Bind(int start, int max) => Step(start, max).Bind(a => Step(a, max)).Bind(b => Step(b, max));

    static Result<int> EarlyReturn(int start, int max)
    {
        var ra = Step(start, max);
        if (!ra.TryGetValue(out var a)) return ra;
        var rb = Step(a, max);
        if (!rb.TryGetValue(out var b)) return rb;
        return Step(b, max);
    }

    [AsyncMethodBuilder(typeof(AsyncResultBuilder<>))]
    static async ValueTask<Result<int>> AwaitValueTask(int start, int max)
    {
        var a = await Step(start, max);
        var b = await Step(a, max);
        return await Step(b, max);
    }

    static Result<int> Sync(ValueTask<Result<int>> task) => task.IsCompletedSuccessfully ? task.Result : throw new InvalidOperationException("pending");

    public static readonly TheoryData<string, int> Inputs = new()
    {
        { "success", 1 },
        { "first step fails", Max + 1 },
        { "last step fails", Max - 1 },
    };

    static readonly (string Style, Func<int, int, Result<int>> Run)[] Styles =
    [
        ("await", Await),
        ("await, unwinding", Unwinding),
        ("Bind", Bind),
        ("early return", EarlyReturn),
        ("await, ValueTask", (s, m) => Sync(AwaitValueTask(s, m))),
    ];

    static long BytesPerCall(Func<int, int, Result<int>> run, int start)
    {
        const int calls = 2_000;
        for (var i = 0; i < calls; i++) _ = run(start, Max);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < calls; i++) _ = run(start, Max);
        return (GC.GetAllocatedBytesForCurrentThread() - before) / calls;
    }

    [Theory]
    [MemberData(nameof(Inputs))]
    public void Bytes_per_call(string name, int start)
    {
        var measured = Styles.ToDictionary(s => s.Style, s => BytesPerCall(s.Run, start));
        output.WriteLine($"{name}: {string.Join(", ", measured.Select(m => $"{m.Key} {m.Value} B"))}");
        // Every style gives the same result.
        Assert.Single(Styles.Select(s => s.Run(start, Max)).Distinct());
#if DEBUG
        // Debug builds make every state machine a class (for Edit and Continue), so each await-style call allocates one.
        Assert.Equal((true, 0L), (measured["await"] > 0, measured["early return"]));
#else
        // Release, fully optimised (TieredCompilation off): the await styles allocate nothing, as early return; Bind
        // allocates its closure over `max`; unwinding allocates its exception on a failure.
        var failed = name != "success";
        Assert.Equal(
            (0L, 0L, 24L, 0L, true),
            (measured["await"], measured["await, ValueTask"], measured["Bind"], measured["early return"],
             failed ? measured["await, unwinding"] is > 200 and < 1000 : measured["await, unwinding"] == 0));
#endif
    }

    /// <summary>An awaitable that never completes synchronously and resumes on the same thread, so a real suspension is measurable here.</summary>
    readonly struct Suspend : ICriticalNotifyCompletion
    {
        public Suspend GetAwaiter() => this;

        public bool IsCompleted => false;

        public void GetResult() { }

        public void OnCompleted(Action continuation) => continuation();

        public void UnsafeOnCompleted(Action continuation) => continuation();
    }

    [AsyncMethodBuilder(typeof(AsyncResultBuilder<>))]
    static async ValueTask<Result<int>> SuspendingAwait(int start, int max)
    {
        await new Suspend();
        var a = await Step(start, max);
        return await Step(a, max);
    }

    static async ValueTask<Result<int>> SuspendingEarlyReturn(int start, int max)
    {
        await new Suspend();
        var ra = Step(start, max);
        if (!ra.TryGetValue(out var a)) return ra;
        return Step(a, max);
    }

    [Fact]
    public async Task A_real_suspension_costs_the_same_as_in_a_plain_async_ValueTask_method()
    {
        foreach (var start in new[] { 1, Max + 1 })
        {
            var custom = BytesPerCall((s, m) => Sync(SuspendingAwait(s, m)), start);
            var plain = BytesPerCall((s, m) => Sync(SuspendingEarlyReturn(s, m)), start);
            output.WriteLine($"start {start}: custom builder {custom} B, AsyncValueTaskMethodBuilder {plain} B");
            // The box holds the state machine, which also holds the result awaiter: a few bytes more, nothing else.
            // Unoptimised (Debug), the builder's `awaiter is IShortCircuitAwaiter` test also boxes the awaiter on a failure.
#if DEBUG
            Assert.InRange(custom - plain, 0, 48);
#else
            Assert.InRange(custom - plain, 0, 16);
#endif
            Assert.Equal(await SuspendingEarlyReturn(start, Max), await SuspendingAwait(start, Max));
        }
    }
}
