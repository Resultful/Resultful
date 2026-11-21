using Resultful.AwaitProbe.Naive;

namespace Resultful.AwaitProbe.Tests;

/// <summary>The hazard the probe's awaiter avoids: one that never resumes hangs a plain async method for ever.</summary>
public sealed class NaiveAwaitTests
{
    [Fact]
    public async Task A_never_resuming_awaiter_leaves_a_plain_async_Task_incomplete_with_no_error()
    {
        var task = Plain(Error.From("failed").Fail<int>());
        await Assert.ThrowsAsync<TimeoutException>(() => task.WaitAsync(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken));
        Assert.Equal(TaskStatus.WaitingForActivation, task.Status);

        Assert.Equal(2, await Plain(1.Ok()));

        static async Task<int> Plain(Result<int> result) => await result * 2;
    }
}
