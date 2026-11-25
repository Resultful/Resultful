using Xunit;
using static Resultful.Analyzers.Tests.AnalyzerHarness;

namespace Resultful.Analyzers.Tests;

/// <summary>RF0008 (typed handlers for a generated multi-case Match, Switch or TryMap), against the real generator's shapes.</summary>
public sealed class HandlerRulesTests
{
    public const string Domain = """
        using System;
        using Resultful;
        using Resultful.CodeAnalysis;

        public sealed record Order(int Id);
        [ClosedHierarchy] public abstract partial record OrderError : Error;
        public sealed record OrderLocked(int Id) : OrderError("locked");
        public sealed record OrderNotFound(int Id) : OrderError("missing");

        [ClosedHierarchy] public abstract partial record LoneError : Error;
        public sealed record Only() : LoneError("only");

        [ResultShape(typeof(OrderError), Success = typeof(Order))]
        public readonly partial struct PlaceOrderResult;

        [ResultShape(typeof(LoneError), Success = typeof(Order))]
        public readonly partial struct LoneResult;

        [ResultShape(typeof(Error), Success = typeof(Order))]
        public readonly partial struct PlainResult;

        [ResultShape(typeof(OrderError), Success = typeof(Order), ManyErrors = true)]
        public readonly partial struct ManyResult;

        """;

    [Fact]
    public void Untyped_handlers_of_a_generated_multi_case_dispatch_are_errors() => Verify(Domain + """
        public static class Cases
        {
            static string Locked(OrderLocked l) => "locked";

            static void M(OrderError e, PlaceOrderResult r, LoneResult lone, PlainResult plain, ManyResult many, Result<Order, OrderError> core,
                Func<OrderNotFound, string> missing)
            {
                _ = e.Match(l => "locked", n => "missing"); // expect: RF0008, RF0008
                _ = e.Match((OrderLocked l) => "locked", (OrderNotFound n) => "missing"); // expect: RF0005
                _ = e.Match(Locked, missing);
                _ = e.Match(Locked, _ => "missing"); // expect: RF0008
                _ = e.Match(onOrderLocked: (OrderLocked l) => "locked", onOrderNotFound: (n) => "missing"); // expect: RF0008
                _ = e.Match(delegate (OrderLocked l) { return "locked"; }, delegate { return "missing"; }); // expect: RF0008
                e.Switch(l => { }, (OrderNotFound n) => { }); // expect: RF0008
                _ = OrderError.TryMap(e, l => 1, n => 2, out var status); // expect: RF0008, RF0008
                _ = r.Match(o => "order", (OrderLocked l) => "locked", (OrderNotFound n) => "missing"); // expect: RF0008
                _ = r.Match((Order o) => "order", (OrderLocked l) => "locked", static (OrderNotFound n) => "missing");
                _ = lone.Match(o => "order", x => "only");
                _ = plain.Match(o => "order", x => "error");
                _ = many.Match(o => "order", xs => "errors");
                _ = core.Match(o => "order", x => "error");
                _ = LoneError.TryMap(e, x => 1, out _);
            }
        }
        """, generate: true);

    [Fact]
    public void A_hand_written_dispatch_on_a_closed_base_is_checked_too() => Verify("""
        using System;
        using Resultful.CodeAnalysis;

        [ClosedHierarchy]
        public abstract record Shape
        {
            private protected Shape() { }
            public T Match<T>(Func<Circle, T> onCircle, Func<Square, T> onSquare) => this is Circle c ? onCircle(c) : onSquare((Square)this);
        }

        public sealed record Circle : Shape;
        public sealed record Square : Shape;

        public static class Cases
        {
            static int M(Shape s) => s.Match(c => 1, (Square q) => 2); // expect: RF0008
        }
        """);
}
