// Decision 15 (17): CS8509 is suppressed on a switch over a [ClosedHierarchy] base that matches every case, and kept when one
// is missing, guarded with `when`, or the base has no attribute.
// spec: uses analyzers
using Resultful;
using Resultful.CodeAnalysis;

[ClosedHierarchy]
public abstract record OrderError : Error { private protected OrderError(string m) : base(m) { } }
public sealed record OrderNotFound() : OrderError("nf");
public sealed record OrderLocked(int Id) : OrderError("locked");
public abstract record OpenError(string M) : Error(M);
public sealed record Only() : OpenError("only");

public static class Cases
{
    static int Every(OrderError e) => e switch { OrderNotFound => 404, OrderLocked => 409 };
    static int Declarations(OrderError e) => e switch { OrderNotFound n => 404, OrderLocked l => 409 };
    static int Missing(OrderError e) => e switch { OrderNotFound => 404 }; // expect: CS8509
    static int Guarded(OrderError e) => e switch { OrderNotFound => 404, OrderLocked l when l.Id > 0 => 409 }; // expect: CS8509
    static int Open(OpenError e) => e switch { Only => 1 }; // expect: CS8509
}
