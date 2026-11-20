// Round 4 (19): the instance SwitchAsync returns a Task the caller awaits; a throwing callback faults it.
// 0.2 returned void, dropping the callback's task and its exception (pinned as a hazard by 01).
using System.Threading.Tasks;
using Resultful;
using Resultful.Spec.Support;

public static class Cases
{
    [SpecCase]
    public static async Task SwitchAsync_is_awaited()
    {
        var done = false;
        Task t = Result.Ok(1).SwitchAsync(async v => { await Task.Delay(10); done = true; }, async e => await Task.Yield());
        await t;
        Check.True(done, "the callback finished before the await returned");
    }

    [SpecCase]
    public static async Task A_throwing_callback_faults_the_task()
    {
        Result<int, string> failed = "bad";
        await Check.ThrowsAsync<System.InvalidOperationException>(
            () => failed.SwitchAsync(v => Task.CompletedTask, e => throw new System.InvalidOperationException(e)), "fault");
    }
}
