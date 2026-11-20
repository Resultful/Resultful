// Quality (16a): RF0001 also covers an unawaited Task or ValueTask of a result, and an async void lambda passed to Switch.
// spec: uses analyzers
using Resultful;
using Resultful.CodeAnalysis;
using System.Threading.Tasks;

public static class Cases
{
    static Task<Result<int>> CancelAsync(int id) => Task.FromResult(Result.Ok(id));
    static ValueTask<Result<int>> CheckAsync(int id) => new(Result.Ok(id));

    static async Task Forgets()
    {
        CancelAsync(1); // expect: RF0001, CS4014
        await CancelAsync(2); // expect: RF0001
        _ = CheckAsync(3);
        Result.Ok(1).Switch(async v => await Task.Yield(), e => { }); // expect: RF0001
    }
}
