// Decision 17 point 5 (16a): RF0002 also flags default of an error union.
// spec: tfm net11.0
// spec: uses analyzers
using Resultful;

public sealed record OrderNotFound(int Id) : Error("nf");
public sealed record OrderLocked(int Id) : Error("locked");
public union OrderError(OrderNotFound, OrderLocked);

public static class Cases
{
    static OrderError A() => default(OrderError); // expect: RF0002
    static OrderError B() => default; // expect: RF0002
    static OrderError C() => new OrderNotFound(1);
}
