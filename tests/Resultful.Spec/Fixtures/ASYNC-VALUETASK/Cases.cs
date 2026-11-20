// M2 async (19): ValueTask receivers on net10.0 and later.
using System.Threading.Tasks;
using Resultful;
using Resultful.Spec.Support;

public static class Cases
{
    static ValueTask<Result<int, string>> Load(int id) => new(id > 0 ? Result.Ok<int, string>(id) : "bad id");

    [SpecCase]
    public static async Task A_ValueTask_chain()
    {
        var text = await Load(4).Map(x => x * 2).Bind(x => Result.Ok<int, string>(x + 1)).Match(v => "ok " + v, e => e);
        Check.Equal("ok 9", text, "chain");
        var failed = await Load(0).MapError(e => e.Length).Match(v => v, e => -e);
        Check.Equal(-6, failed, "error mapped");
    }
}
