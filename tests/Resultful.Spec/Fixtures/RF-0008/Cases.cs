// RF0008 (16b, error): handlers passed to a generated multi-case Match, Switch or TryMap are typed or method groups,
// so reordering the leaves can't silently swap them. Core's two-handler Match is exempt.
// spec: uses generator
// spec: uses analyzers
using Resultful;
using Resultful.CodeAnalysis;

[ClosedHierarchy] public abstract partial record OrderError : Error;
public sealed record OrderNotFound(int Id) : OrderError("nf");
public sealed record OrderLocked(int Id) : OrderError("locked");
public sealed record Order(int Id);
[ResultShape(typeof(OrderError), Success = typeof(Order))] public readonly partial struct PlaceOrderResult;

public static class Cases
{
    static int Locked(OrderLocked l) => 409;
    static int Untyped(PlaceOrderResult r) => r.Match(o => 201, n => 404, l => 409); // expect: RF0008
    static int Typed(PlaceOrderResult r) => r.Match((Order o) => 201, Locked, (OrderNotFound n) => 404);
    static int Core(Result<int> r) => r.Match(v => v, e => 0);
}
// expect-fix: RF0008
