// M2 async (19): IAsyncEnumerable helpers: CollectAsync stops at the first failure, PartitionAsync reads everything.
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resultful;
using Resultful.Spec.Support;

public static class Cases
{
    static async IAsyncEnumerable<Result<int, string>> Stream(params object[] items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item is int i ? Result.Ok<int, string>(i) : (string)item;
        }
    }

    [SpecCase]
    public static async Task Collect_and_partition()
    {
        var all = await Stream(1, 2, 3).CollectAsync(CancellationToken.None);
        Check.SequenceEqual(new[] { 1, 2, 3 }, all.Value, "all values");
        var first = await Stream(1, "a", 2, "b").CollectAsync(CancellationToken.None);
        Check.Equal("a", first.Error, "first failure");
        var (values, errors) = await Stream(1, "a", 2, "b").PartitionAsync(CancellationToken.None);
        Check.SequenceEqual(new[] { 1, 2 }, values, "values");
        Check.SequenceEqual(new[] { "a", "b" }, errors, "errors");
    }
}
