using OneOf.Types;
using Xunit;

namespace Resultful.Consumption;

public sealed class OptionTests
{
    [Fact]
    public async Task Option_instance_members_and_conversions()
    {
        var fromCtor = new Option<int>();
        Assert.Equal(0, fromCtor.Or(-1));

        Option<int> some = 10;
        Option<int> none = new None();
        var staticNone = Option<int>.None;

        Assert.True(some.ToOneOf().IsT0);
        Assert.True(none.ToOneOf().IsT1);
        Assert.True(staticNone.ToOneOf().IsT1);

        Assert.Equal(20, some.Bind(x => (x * 2).Some()).Or(-1));
        Assert.Equal(20, (await some.BindAsync(x => Task.FromResult((x * 2).Some()))).Or(-1));
        Assert.Equal(20, some.Map(x => x * 2).Or(-1));
        Assert.Equal(20, (await some.MapAsync(x => Task.FromResult(x * 2))).Or(-1));

        Option<object> boxed = ((object)"hi").Some();
        Assert.Equal("hi", boxed.CastError<string>().Or(""));

        Assert.Equal(15, some.Fold(5, (acc, x) => acc + x));
        Assert.Equal(15, await some.FoldAsync(5, (acc, x) => Task.FromResult(acc + x)));
        Assert.Equal(15, some.FoldUntil(5, (acc, x) => (acc + x).Some()).Or(-1));
        Assert.Equal(15, (await some.FoldUntilAsync(5, (acc, x) => Task.FromResult((acc + x).Some()))).Or(-1));

        Assert.Equal(10, some.Match(x => x, _ => -1));
        Assert.Equal(10, await some.MatchAsync(x => Task.FromResult(x), _ => Task.FromResult(-1)));
        var hit = 0;
        some.Switch(x => hit = x, _ => hit = -1);
        Assert.Equal(10, hit);
        await some.SwitchAsync(x => { hit = x + 1; return Task.CompletedTask; }, _ => Task.CompletedTask);
        Assert.Equal(11, hit);

        some.Tee(x => hit = x * 2);
        Assert.Equal(20, hit);
        await some.TeeAsync(x => { hit = x * 3; return Task.CompletedTask; });
        Assert.Equal(30, hit);

        Assert.Equal(99, none.Or(99));
        Assert.Equal(99, none.Or(() => 99));
        Assert.Equal(99, none.Or(99.Some()).Or(-1));
        Assert.Equal(99, none.Or(() => 99.Some()).Or(-1));
        Assert.Equal(99, await none.OrAsync(Task.FromResult(99)));
        Assert.Equal(99, await none.OrAsync(() => Task.FromResult(99)));
        Assert.Equal(99, (await none.OrAsync(Task.FromResult(99.Some()))).Or(-1));
        Assert.Equal(99, (await none.OrAsync(() => Task.FromResult(99.Some()))).Or(-1));
    }

    [Fact]
    public async Task Option_static_sync_and_async_extensions()
    {
        var taskOpt = Task.FromResult(10.Some());
        var taskNone = Task.FromResult(Option<int>.None);

        Assert.Equal(10, (await Task.FromResult(10).Some()).Or(-1));
        Assert.Equal(10, 10.ToOption().Or(-1));
        Assert.Equal(10, (await Task.FromResult(10).ToOption()).Or(-1));

        Assert.Equal(20, (await taskOpt.Bind(x => (x * 2).Some())).Or(-1));
        Assert.Equal(20, (await taskOpt.BindAsync(x => Task.FromResult((x * 2).Some()))).Or(-1));
        Assert.Equal(20, (await taskOpt.Map(x => x * 2)).Or(-1));
        Assert.Equal(20, (await taskOpt.MapAsync(x => Task.FromResult(x * 2))).Or(-1));

        Assert.Equal(10, 10.Some().Some().Flatten().Or(-1));
        Assert.Equal(10, (await Task.FromResult(10.Some().Some()).Flatten()).Or(-1));

        Assert.Equal(10, await taskOpt.Match(x => x, _ => -1));
        Assert.Equal(10, await taskOpt.MatchAsync(x => Task.FromResult(x), _ => Task.FromResult(-1)));
        var hit = 0;
        await taskOpt.Switch(x => hit = x, _ => hit = -1);
        Assert.Equal(10, hit);
        await taskOpt.SwitchAsync(x => { hit = x + 1; return Task.CompletedTask; }, _ => Task.CompletedTask);
        Assert.Equal(11, hit);
        await taskOpt.Tee(x => hit = x + 2);
        await taskOpt.TeeAsync(x => { hit = x + 3; return Task.CompletedTask; });
        Assert.True((await taskOpt.ToOneOf()).IsT0);

        Assert.Equal(5, await taskNone.Or(5));
        Assert.Equal(5, await taskNone.Or(() => 5));
        Assert.Equal(5, (await taskNone.Or(5.Some())).Or(-1));
        Assert.Equal(5, (await taskNone.Or(() => 5.Some())).Or(-1));
        Assert.Equal(5, await taskNone.OrAsync(Task.FromResult(5)));
        Assert.Equal(5, await taskNone.OrAsync(() => Task.FromResult(5)));
        Assert.Equal(5, (await taskNone.OrAsync(Task.FromResult(5.Some()))).Or(-1));
        Assert.Equal(5, (await taskNone.OrAsync(() => Task.FromResult(5.Some()))).Or(-1));

        // Plus
        Assert.Equal((1, 2), 1.Some().Plus(2.Some()).Or((0, 0)));
        Assert.Equal(3, 1.Some().Plus(2.Some(), (a, b) => a + b).Or(0));
        Assert.Equal(new AddableInt(5), new AddableInt(2).Some().Plus<AddableInt, AddableInt, AddableInt>(new AddableInt(3).Some()).Or(default(AddableInt)));

        // Fold / FoldAsync / FoldUntil / FoldUntilAsync (Task & IEnumerable)
        Assert.Equal(15, await taskOpt.Fold(5, (a, b) => a + b));
        Assert.Equal(15, await taskOpt.FoldAsync(5, (a, b) => Task.FromResult(a + b)));
        Assert.Equal(15, (await taskOpt.FoldUntil(5, (a, b) => (a + b).Some())).Or(-1));
        Assert.Equal(15, (await taskOpt.FoldUntilAsync(5, (a, b) => Task.FromResult((a + b).Some()))).Or(-1));

        IEnumerable<Option<int>> items = [1.Some(), 2.Some(), 3.Some()];
        Assert.Equal(6, items.Fold(0, (a, b) => a + b));
        Assert.Equal(6, items.Fold(0.Some(), (a, b) => a + b).Or(-1));
        Assert.Equal(6, await items.FoldAsync(0, (a, b) => Task.FromResult(a + b)));
        Assert.Equal(6, (await items.FoldAsync(0.Some(), (a, b) => Task.FromResult(a + b))).Or(-1));
        Assert.Equal(6, items.FoldUntil(0, (a, b) => (a + b).Some()));
        Assert.Equal(6, items.FoldUntil(0.Some(), (a, b) => (a + b).Some()).Or(-1));
        Assert.Equal(6, await items.FoldUntilAsync(0, (a, b) => Task.FromResult((a + b).Some())));
        Assert.Equal(6, (await items.FoldUntilAsync(0.Some(), (a, b) => Task.FromResult((a + b).Some()))).Or(-1));

        // Reduce / ReduceAsync / ReduceUntil / TryReduce*
        Assert.Equal(6, items.Reduce((a, b) => a + b).Or(-1));
        Assert.Equal(new AddableInt(5), new[] { new AddableInt(2).Some(), new AddableInt(3).Some() }.Reduce().Or(default(AddableInt)));
        Assert.Equal(6, (await items.ReduceAsync((a, b) => Task.FromResult(a + b))).Or(-1));
        Assert.Equal(6, items.ReduceUntil((a, b) => (a + b).Some()).Or(-1));
        Assert.Equal(6, (await items.ReduceUntil((a, b) => Task.FromResult((a + b).Some()))).Or(-1));

        Assert.Equal(6, items.TryReduce((a, b) => a + b).Or(-1));
        Assert.Equal(6, (await items.TryReduceAsync((a, b) => Task.FromResult(a + b))).Or(-1));
        Assert.Equal(6, items.TryReduceUntil((a, b) => (a + b).Some()).Or(-1));
        Assert.Equal(6, (await items.TryReduceUntilAsync((a, b) => Task.FromResult((a + b).Some()))).Or(-1));

        // Unroll / UnwrapAsync / WhenAll
        Assert.Equal([1, 2, 3], items.Unroll().Or([]).ToArray());
        Assert.Equal(42, (await Option.Some<Task<int>>(Task.FromResult(42)).UnwrapAsync()).Or(-1));
        IEnumerable<Option<Task<int>>> asyncOpts = [Option.Some<Task<int>>(Task.FromResult(1)), Option.Some<Task<int>>(Task.FromResult(2))];
        var allOpts = await asyncOpts.WhenAll();
        Assert.Equal([1, 2], allOpts.Or([]).ToArray());
    }
}
