// M2 async (19): a CancellationToken flows to each async step that takes one.
using System.Threading;
using System.Threading.Tasks;
using Resultful;
using Resultful.Spec.Support;

public static class Cases
{
    [SpecCase]
    public static async Task The_token_reaches_the_step()
    {
        using var cts = new CancellationTokenSource();
        CancellationToken seen = default;
        await Result.Ok(1).TeeAsync((x, ct) => { seen = ct; return Task.CompletedTask; }, cts.Token);
        Check.Equal(cts.Token, seen, "TeeAsync");
        var r = await Result.Ok<int, string>(2).BindAsync((x, ct) => Task.FromResult(Result.Ok<int, string>(x + (ct == cts.Token ? 1 : 0))), cts.Token);
        Check.Equal(3, r.Value, "BindAsync");
    }
}
