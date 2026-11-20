// GEN-DIAG (13a): RFG0003, an open generic type derives from a [ClosedHierarchy] base.
// spec: uses generator
using Resultful;
using Resultful.CodeAnalysis;

[ClosedHierarchy] public abstract partial record OrderError : Error;
public sealed record OrderNotFound(int Id) : OrderError($"order {Id} was not found");
public sealed record Wrapped<T>(T Value) : OrderError("wrapped"); // expect: RFG0003
