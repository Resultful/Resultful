// GEN-DIAG (13a): RFG0002 (warning), a concrete type inside a [ClosedHierarchy] that isn't sealed.
// spec: uses generator
using Resultful;
using Resultful.CodeAnalysis;

[ClosedHierarchy] public abstract partial record OrderError : Error;
public sealed record OrderNotFound(int Id) : OrderError($"order {Id} was not found");
public record OrderLocked(int Id) : OrderError($"order {Id} is locked"); // expect: RFG0002
