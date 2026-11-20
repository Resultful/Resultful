// RF0007 (16b, info): a [ClosedHierarchy] base whose constructor another assembly could call. Fix: make it private protected.
// spec: uses analyzers
using Resultful;
using Resultful.CodeAnalysis;

[ClosedHierarchy]
public abstract record OrderError : Error
{
    protected OrderError(string message) : base(message) { } // expect: RF0007
}
public sealed record OrderLocked() : OrderError("locked");
// expect-fix: RF0007
