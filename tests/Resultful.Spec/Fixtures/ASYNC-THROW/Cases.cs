// Quality (19): exceptions propagate. Nothing in the core catches implicitly; Result.Try is the opt-in.
using System.Threading;
using System.Threading.Tasks;
using Resultful;
using Resultful.Spec.Support;

public sealed class Boom() : System.Exception("boom");

public static class Cases
{
    [SpecCase]
    public static async Task A_throwing_async_step_throws_and_doesnt_become_a_failure()
    {
        await Check.ThrowsAsync<Boom>(() => Result.Ok(1).BindAsync<int>((x, ct) => throw new Boom(), CancellationToken.None), "BindAsync with a token");
        await Check.ThrowsAsync<Boom>(async () => await new ValueTask<Result<int>>(Result.Ok(1)).Map<int, int>(x => throw new Boom()), "ValueTask Map");
    }

    [SpecCase]
    public static void A_throwing_sync_step_throws()
    {
        Check.Throws<Boom>(() => Result.Ok(1).Map<int>(x => throw new Boom()).IsSuccess.ToString(), "Map");
    }
}
