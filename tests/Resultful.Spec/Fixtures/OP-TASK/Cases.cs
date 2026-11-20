// Decision 9 (18): the same operators work on Task<Result<T>>, with async steps.
using System.Threading.Tasks;
using Resultful;
using Resultful.Operators;
using Resultful.Spec.Support;

public static class Cases
{
    static Task<Result<int>> Load(bool ok) => Task.FromResult(ok ? Result.Ok(3) : Result.Fail<int>("gone"));

    [SpecCase]
    public static async Task Operators_on_a_task()
    {
        Check.Equal(99, await (Load(false) | 99), "task | value");
        Check.Equal(3, (await (Load(false) | Result.Ok(3))).Value, "task | result");
        var s = await (Load(true) >> (async x => { await Task.Yield(); return Result.Ok(x * 2); }));
        Check.Equal(6, s.Value, "task >> async step");
    }
}
