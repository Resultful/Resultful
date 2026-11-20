// Quality (19): cancellation propagates as OperationCanceledException, including through Result.TryAsync.
using System.Threading;
using System.Threading.Tasks;
using Resultful;
using Resultful.Spec.Support;

public static class Cases
{
    [SpecCase]
    public static async Task A_cancelled_step_throws()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Check.ThrowsAsync<System.OperationCanceledException>(
            () => Result.Ok(1).MapAsync(async (x, ct) => { await Task.Delay(1000, ct); return x; }, cts.Token), "MapAsync");
    }

    [SpecCase]
    public static async Task TryAsync_doesnt_catch_cancellation()
    {
        await Check.ThrowsAsync<System.OperationCanceledException>(
            () => Result.TryAsync<int>(() => throw new System.OperationCanceledException(), ex => Error.From(ex.Message)), "TryAsync");
    }
}
