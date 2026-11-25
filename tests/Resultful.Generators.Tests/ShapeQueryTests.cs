using System.Reflection;
using CsCheck;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Resultful.Generators.Tests;

// Decision 22 item 3: query syntax over generated shapes. The fixture and a probe are compiled with the generator and
// run against the real core; the law holds a query to the Bind chain it stands for on random ids. A shape starts a query
// through its own members, and is a later step (or precedes an async one) through the core's ShapeLinqExtensions. The
// query gives the core Result<TOut, TError>, which converts back to any shape with that error where it is returned.
public sealed class ShapeQueryTests
{
    private const string Shop = """
        using Resultful;
        using Resultful.CodeAnalysis;

        namespace Fixture.Query
        {
            [ClosedHierarchy]
            public abstract partial record ShopError(string Message) : Error(Message);
            public sealed record NotFound(int Id) : ShopError("order not found");
            public sealed record OutOfStock(int Id) : ShopError("out of stock");

            public sealed record Order(int Id, int Quantity);
            public sealed record Stock(int OrderId, int Left);
            public sealed record Receipt(int OrderId, int Left);

            [ResultShape(typeof(ShopError), Success = typeof(Order))]
            public readonly partial struct GetOrderResult;

            [ResultShape(typeof(ShopError), Success = typeof(Stock))]
            public readonly partial struct ReserveResult;

            [ResultShape(typeof(ShopError), Success = typeof(Receipt))]
            public readonly partial struct PlaceOrderResult;

            [ResultShape(typeof(ShopError))]
            public readonly partial struct CancelResult;
        }
        """;

    // System.Linq is imported on purpose: its Select and SelectMany are over IEnumerable and must not compete.
    private const string Probe = """
        using System;
        using System.Linq;
        using System.Threading.Tasks;
        using Resultful;
        using Resultful.LINQ;
        using Fixture.Query;

        public static class QueryProbe
        {
            // CsCheck samples on several threads.
            [ThreadStatic] public static int Reserved;

            static GetOrderResult Get(int id) => id > 0 ? new Order(id, id % 4) : new NotFound(id);

            static ReserveResult Reserve(Order order)
            {
                Reserved++;
                return order.Quantity > 0 ? new Stock(order.Id, 10 - order.Quantity) : new OutOfStock(order.Id);
            }

            static CancelResult Cancel(int id) => id % 3 == 0 ? new NotFound(id) : CancelResult.Ok();

            static async Task<Result<Stock, ShopError>> ReserveAsync(Order order)
            {
                await Task.Yield();
                return Reserve(order);
            }

            // Shape then shape: ShapeLinqExtensions reads the second through IResultShape.
            static PlaceOrderResult Query(int id) =>
                from order in Get(id)
                from stock in Reserve(order)
                let left = stock.Left
                select new Receipt(order.Id, left);

            static PlaceOrderResult Chain(int id) =>
                Get(id).Bind(order => Reserve(order).AsResult().Map(stock => new Receipt(order.Id, stock.Left)));

            static Result<int, ShopError> Select(int id) => from order in Get(id) select order.Quantity;

            static Result<int, ShopError> Valueless(int id) =>
                from _ in Cancel(id)
                from order in Get(id)
                select order.Id;

            // Shape then a Task step.
            static async Task<PlaceOrderResult> QueryAsync(int id) =>
                await (from order in Get(id)
                       from stock in ReserveAsync(order)
                       select new Receipt(order.Id, stock.Left));

            // A core result then a shape.
            static Result<int, ShopError> CoreThenShape(int id) =>
                from order in Get(id).AsResult()
                from stock in Reserve(order)
                select stock.Left;

            // The same shape twice, and the query converts back to it.
            static GetOrderResult Same(int id) =>
                from a in Get(id)
                from b in Get(a.Id + 1)
                select new Order(b.Id, a.Quantity);

            // A Task step, then a shape.
            static async Task<PlaceOrderResult> AsyncThenShape(int id) =>
                await (from order in Get(id)
                       from first in ReserveAsync(order)
                       from again in Reserve(order)
                       select new Receipt(order.Id, again.Left));

            static string Show(PlaceOrderResult r) => r.Match(x => $"receipt {x.OrderId} {x.Left}", e => "missing " + e.Id, e => "stock " + e.Id);

            static string Show<T>(Result<T, ShopError> r) => r.Match(x => "ok " + x, e => e.GetType().Name);

            public static string Law(int id)
            {
                Reserved = 0;
                var query = Show(Query(id));
                var queryCalls = Reserved;
                Reserved = 0;
                var chain = Show(Chain(id));
                return query == chain && queryCalls == Reserved ? "same " + query + " " + queryCalls : $"differ {query} / {chain}";
            }

            public static string Others(int id) =>
                $"{Show(Select(id))}; {Show(Valueless(id))}; {Show(QueryAsync(id).GetAwaiter().GetResult())}; " +
                $"{Show(CoreThenShape(id))}; {Show(Same(id).Map(o => o.Id * 10 + o.Quantity))}; {Show(AsyncThenShape(id).GetAwaiter().GetResult())}";
        }
        """;

    private static readonly Lazy<Type> Loaded = new(() =>
        RuntimeTests.Compile([("Shop.cs", Shop), ("Probe.cs", Probe)], "ShapeQuery").GetType("QueryProbe", throwOnError: true)!);

    private static string Call(string method, int id) => (string)Loaded.Value.GetMethod(method)!.Invoke(null, [id])!;

    [Fact]
    public void A_query_from_a_shape_equals_its_bind_chain_and_stops_at_the_first_failure() =>
        Gen.Int[-3, 12].Sample(id =>
        {
            var law = Call("Law", id);
            Assert.StartsWith("same ", law, StringComparison.Ordinal);
            // Reserve runs only when Get found the order.
            Assert.EndsWith(id > 0 ? " 1" : " 0", law, StringComparison.Ordinal);
        });

    [Theory]
    [InlineData(5, "ok 1; ok 5; receipt 5 9; ok 9; ok 61; receipt 5 9")]
    [InlineData(4, "ok 0; ok 4; stock 4; OutOfStock; ok 50; stock 4")]
    [InlineData(3, "ok 3; NotFound; receipt 3 7; ok 7; ok 43; receipt 3 7")]
    [InlineData(-1, "NotFound; NotFound; missing -1; NotFound; NotFound; missing -1")]
    public void Shapes_compose_with_shapes_core_results_and_async_steps_in_any_position(int id, string expected) =>
        Assert.Equal(expected, Call("Others", id));

    [Fact]
    public void A_shape_gathering_errors_has_no_query_syntax()
    {
        // Decision 22: no query syntax over Validation, so a ManyErrors shape can't start a query (CS1936).
        const string Misuse = """
            using Resultful;
            using Resultful.LINQ;
            using Fixture.Many;

            public static class Misuse
            {
                static RegisterResult Register() => new Registration("a@b.c");

                public static object Many() => from r in Register() select r.Email;
            }
            """;

        var comp = GeneratorTestHarness.CreateCompilation([("Many.cs", FixtureCatalog.ManyErrorsShape), ("Misuse.cs", Misuse)]);
        var output = GeneratorTestHarness.Run(comp, out _);
        var errors = output.CompilationDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Id);
        Assert.Equal(["CS1936"], errors);
    }
}
