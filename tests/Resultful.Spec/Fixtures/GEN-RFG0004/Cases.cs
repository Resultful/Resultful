// GEN-DIAG (13a): RFG0004, a public shape exposes a leaf less accessible than the shape (CS0051 otherwise).
// spec: uses generator
using Resultful;
using Resultful.CodeAnalysis;

[ClosedHierarchy] public abstract partial record OrderError : Error;
public sealed record OrderNotFound(int Id) : OrderError($"order {Id} was not found");
internal sealed record Secret() : OrderError("secret");
[ResultShape(typeof(OrderError), Success = typeof(int))] public readonly partial struct LeakyResult; // expect: RFG0004
[ResultShape(typeof(OrderError), Success = typeof(int))] internal readonly partial struct InternalResult;
