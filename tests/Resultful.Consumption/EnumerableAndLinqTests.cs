using Resultful.LINQ;
using Xunit;

namespace Resultful.Consumption;

public sealed class EnumerableAndLinqTests
{
    [Fact]
    public async Task EnumerableExtensions_sync_and_async()
    {
        IEnumerable<int> nums = [1, 2, 3, 4];

        Assert.Equal(10, nums.Fold(0, (a, b) => a + b));
        Assert.Equal(10, await nums.FoldAsync(0, (a, b) => Task.FromResult(a + b)));
        Assert.Equal(3, nums.FoldUntil(0, (a, b) => b == 3 ? Option<int>.None : (a + b).Some()));
        Assert.Equal(3, await nums.FoldUntilAsync(0, (a, b) => Task.FromResult(b == 3 ? Option<int>.None : (a + b).Some())));
        Assert.Equal(3, await nums.FoldUntilAsync(Task.FromResult(0), (a, b) => Task.FromResult(b == 3 ? Option<int>.None : (a + b).Some())));

        Assert.Equal(10, nums.Reduce((a, b) => a + b));
        Assert.Equal(10, await nums.ReduceAsync((a, b) => Task.FromResult(a + b)));
        Assert.Equal(3, nums.ReduceUntil((a, b) => b == 3 ? Option<int>.None : (a + b).Some()));
        Assert.Equal(3, await nums.ReduceUntilAsync((a, b) => Task.FromResult(b == 3 ? Option<int>.None : (a + b).Some())));

        Assert.Equal(10, nums.TryReduce((a, b) => a + b).Or(-1));
        Assert.Equal(10, (await nums.TryReduceAsync((a, b) => Task.FromResult(a + b))).Or(-1));
        Assert.Equal(3, nums.TryReduceUntil((a, b) => b == 3 ? Option<int>.None : (a + b).Some()).Or(-1));
        Assert.Equal(3, (await nums.TryReduceUntilAsync((a, b) => Task.FromResult(b == 3 ? Option<int>.None : (a + b).Some()))).Or(-1));
    }

    [Fact]
    public void LinqExtensions_query_and_method_syntax_for_Result_and_Option()
    {
        // Result<T>
        var r = from a in 2.Ok()
                from b in (a + 3).Ok()
                select a * b;
        Assert.Equal(10, r.Match(v => v, _ => -1));
        Assert.Equal(6, 2.Ok().Select(x => x * 3).Match(v => v, _ => -1));
        Assert.Equal(5, 2.Ok().SelectMany(x => (x + 3).Ok()).Match(v => v, _ => -1));

        // Result<T, TError>
        var rg = from a in 2.Ok<int, TestError>()
                 from b in (a + 3).Ok<int, TestError>()
                 select a * b;
        Assert.Equal(10, rg.Match(v => v, _ => -1));
        Assert.Equal(6, 2.Ok<int, TestError>().Select(x => x * 3).Match(v => v, _ => -1));
        Assert.Equal(5, 2.Ok<int, TestError>().SelectMany(x => (x + 3).Ok<int, TestError>()).Match(v => v, _ => -1));

        // Option<T>
        var o = from a in 2.Some()
                from b in (a + 3).Some()
                select a * b;
        Assert.Equal(10, o.Or(-1));
        Assert.Equal(6, 2.Some().Select(x => x * 3).Or(-1));
        Assert.Equal(5, 2.Some().SelectMany(x => (x + 3).Some()).Or(-1));
    }
}
