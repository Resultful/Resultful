// Decision 15 (17): a nullable input needs a null arm. The probe's suppressor hid CS8509 without one; the rule keeps it.
// spec: uses analyzers
using Resultful;
using Resultful.CodeAnalysis;

[ClosedHierarchy]
public abstract record OrderError : Error { private protected OrderError(string m) : base(m) { } }
public sealed record OrderNotFound() : OrderError("nf");

public static class Cases
{
    static int NoNullArm(OrderError? e) => e switch { OrderNotFound => 404 }; // expect: CS8509
    static int WithNullArm(OrderError? e) => e switch { OrderNotFound => 404, null => 0 };
}
